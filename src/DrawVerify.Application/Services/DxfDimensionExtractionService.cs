using System.Diagnostics;
using System.Text.Json;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class DxfDimensionExtractionService
{
    public const string TimeoutMessage = "Dimension extraction failed: operation exceeded the configured timeout.";
    public const string GenericFailureMessage = "Dimension extraction failed: unable to complete the request.";

    private readonly IDocumentRepository _documents;
    private readonly IDxfUploadFileStore _files;
    private readonly DxfDimensionExtractionConfiguration _configuration;
    private readonly IDxfDimensionParser _parser;
    private readonly ISessionAuthenticationService _session;
    private readonly IUserRepository? _users;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;

    public DxfDimensionExtractionService(
        IDocumentRepository documents,
        IDxfUploadFileStore files,
        IDxfDimensionParser parser,
        DxfDimensionExtractionConfiguration configuration,
        ISessionAuthenticationService? session = null,
        IUserRepository? users = null,
        ICentralizedLogger? logger = null,
        RoleAuthorization? authorization = null)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _session = session ?? new SessionAuthenticationService();
        _users = users;
        _logger = logger;
        _authorization = authorization ?? new RoleAuthorization();
        _configuration.Validate();
    }

    public DxfDimensionExtractionService(
        IDocumentRepository documents,
        IDxfUploadFileStore files,
        DxfDimensionExtractionConfiguration configuration,
        IDxfDimensionParser parser,
        ISessionAuthenticationService? session = null,
        IUserRepository? users = null,
        ICentralizedLogger? logger = null,
        RoleAuthorization? authorization = null)
        : this(documents, files, parser, configuration, session, users, logger, authorization)
    {
    }

    public bool CanExtractDimensions => _session.IsAuthenticated &&
        _authorization.IsAllowed(_session.CurrentUser, RolePermission.TriggerDxfDimensionExtraction);

    public bool CanViewDimensions => _session.IsAuthenticated &&
        _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewDxfDimensionResults);

    public Task<DxfDimensionExtractionResult> ExtractAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        ExtractDimensionsAsync(new DxfDimensionExtractionRequest(documentId, cancellationToken));

    public Task<DxfDimensionExtractionResult> ExtractDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        ExtractAsync(documentId, cancellationToken);

    public async Task<DxfDimensionExtractionResult> ExtractDimensionsAsync(DxfDimensionExtractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await RefreshAuthorizationAsync(request.CancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Unable to verify DXF dimension authorization.", exception);
        }
        if (!CanExtractDimensions)
            return DxfDimensionExtractionResult.Failure("The Maker or Checker role is required to extract DXF dimensions.");

        Document? document;
        try
        {
            document = await _documents.GetAsync(request.DocumentId, request.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF dimension extraction document lookup", $"Unable to load documentId={request.DocumentId}.");
            throw new InvalidOperationException(GenericFailureMessage, exception);
        }
        if (document is null)
            return DxfDimensionExtractionResult.Failure("Dimension extraction failed: document was not found.");
        if (document.DocumentType != DrawVerify.Core.Enums.DocumentType.DxfDrawing || document.UploadStatus != DrawVerify.Core.Enums.UploadStatus.Uploaded)
            return DxfDimensionExtractionResult.Failure("Dimension extraction requires an uploaded DXF drawing.");

        DocumentVersion? version;
        try
        {
            version = _documents.GetCurrentVersion(document.DocumentId) ?? document.Version;
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF dimension extraction version lookup", $"Unable to load the current version for documentId={document.DocumentId}.");
            throw new InvalidOperationException(GenericFailureMessage, exception);
        }
        if (version is null || version.DocumentId != document.DocumentId)
            return DxfDimensionExtractionResult.Failure("Dimension extraction failed: document version was not found.");

        var stopwatch = Stopwatch.StartNew();
        var operationStage = "storage";
        try
        {
            if (version.FileSizeBytes > _configuration.MaximumFileSizeBytes)
                return await PersistFailureAsync(document, version, "the DXF file exceeds the configured maximum size", stopwatch, request.CancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_configuration.TimeoutSeconds));
            operationStage = "storage";
            await using var source = await _files.OpenReadAsync(version.StoredRelativePath, timeout.Token).ConfigureAwait(false);
            await using var bounded = new SizeLimitedReadStream(source, _configuration.MaximumFileSizeBytes);
            operationStage = "parse";
            var parsed = await _parser.ParseAsync(bounded, document.DocumentId, version.VersionId, timeout.Token).ConfigureAwait(false);

            if (!parsed.Succeeded)
            {
                var error = UserFailure("unable to parse DXF file.");
                _logger?.Log(
                    new InvalidDataException(parsed.ErrorMessage ?? "DXF parser returned a fatal result."),
                    "DXF dimension extraction",
                    Context(document, version, $"fatal parser result: {parsed.ErrorMessage ?? "unknown parser error"}"));
                return await PersistFailureAsync(document, version, error, stopwatch, timeout.Token, parsed.Warnings, parsed.ErrorMessage).ConfigureAwait(false);
            }

            LogWarnings(parsed.Warnings, document.DocumentId, version.VersionId);
            if (parsed.Dimensions.Count == 0)
                _logger?.Log(new InvalidDataException("DXF drawing contains no dimension entities."), "DXF dimension extraction information", Context(document, version, "no dimensions found"));

            timeout.Token.ThrowIfCancellationRequested();
            operationStage = "persistence";
            var action = parsed.IsPartial ? DocumentAuditAction.DimensionsExtractedPartial : DocumentAuditAction.DimensionsExtracted;
            await _documents.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, parsed.Dimensions,
                VerificationStatus.DimensionsExtracted, null,
                () => CreateAudit(document, action, stopwatch.ElapsedMilliseconds, parsed.Dimensions.Count, parsed.Warnings, null),
                timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            stopwatch.Stop();

            var updated = document with { VerificationStatus = VerificationStatus.DimensionsExtracted, VerificationError = null };
            return new DxfDimensionExtractionResult(parsed.Dimensions, parsed.Warnings)
            {
                Document = updated,
                Message = parsed.Dimensions.Count == 0
                    ? "No dimension entities were found in the DXF drawing."
                    : $"Extracted {parsed.Dimensions.Count} dimension(s) from the DXF drawing."
            };
        }
        catch (OperationCanceledException exception)
        {
            var detail = request.CancellationToken.IsCancellationRequested ? "operation was cancelled" : "operation exceeded the configured timeout";
            _logger?.Log(exception, "DXF dimension extraction timeout", Context(document, version, detail));
            var failure = await PersistFailureAsync(document, version, request.CancellationToken.IsCancellationRequested
                ? UserFailure("operation was cancelled")
                : TimeoutMessage, stopwatch, CancellationToken.None).ConfigureAwait(false);
            stopwatch.Stop();
            return failure;
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF dimension extraction", Context(document, version, "parser or storage failure"));
            try
            {
                var failure = await PersistFailureAsync(document, version, FailureMessage(operationStage, exception), stopwatch, CancellationToken.None, diagnosticMessage: exception.Message).ConfigureAwait(false);
                stopwatch.Stop();
                return failure;
            }
            catch (Exception persistenceException)
            {
                stopwatch.Stop();
                _logger?.Log(persistenceException, "DXF dimension extraction persistence", Context(document, version, "unable to persist failure outcome"));
                throw new InvalidOperationException(GenericFailureMessage, persistenceException);
            }
        }
    }

    public async Task<IReadOnlyList<DimensionData>> GetDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await RefreshAuthorizationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Unable to verify DXF dimension authorization.", exception);
        }
        if (!CanViewDimensions)
            throw new UnauthorizedAccessException("The Maker or Checker role is required to view DXF dimensions.");

        try
        {
            var document = await _documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Document was not found.");
            if (document.DocumentType != DrawVerify.Core.Enums.DocumentType.DxfDrawing)
                throw new InvalidOperationException("Dimension results are available only for DXF drawings.");
            return await _documents.GetDimensionsAsync(documentId, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException exception) when (exception.Message == "Dimension results are available only for DXF drawings.")
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF dimension results", $"Unable to load dimension results for documentId={documentId}.");
            throw new InvalidOperationException("Unable to load DXF dimension results.", exception);
        }
    }

    public Task<IReadOnlyList<DimensionData>> QueryDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        GetDimensionsAsync(documentId, cancellationToken);

    private async Task RefreshAuthorizationAsync(CancellationToken cancellationToken)
    {
        var current = _session.CurrentUser;
        if (current is null || _users is null)
            return;

        try
        {
            var account = await _users.FindByUsernameAsync(current.Username, cancellationToken).ConfigureAwait(false);
            if (account is null || !account.IsActive)
                throw new UnauthorizedAccessException("The authenticated user is no longer active.");
            _session.ReplaceCurrentUserRoles(current.Username, account.Roles, account.RoleVersion);
            if (_session is SessionAuthenticationService concreteSession)
                concreteSession.SignIn(new AuthenticatedUser(account.Username, account.Roles, account.RoleVersion)
                {
                    UserId = account.Id.ToString(),
                    DisplayName = account.DisplayName
                });
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF dimension authorization refresh", $"Unable to refresh persisted roles for '{current.Username}'.");
            throw;
        }
    }

    private async Task<DxfDimensionExtractionResult> PersistFailureAsync(
        Document document,
        DocumentVersion version,
        string message,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? warnings = null,
        string? diagnosticMessage = null)
    {
        var error = message.StartsWith("Dimension extraction failed:", StringComparison.Ordinal)
            ? message
            : UserFailure(message);
        await _documents.ReplaceDimensionsAsync(document.DocumentId, version.VersionId, Array.Empty<DimensionData>(),
            VerificationStatus.Error, error,
            () => CreateAudit(document, DocumentAuditAction.DimensionExtractionFailed, stopwatch.ElapsedMilliseconds, 0, warnings, error),
            cancellationToken).ConfigureAwait(false);
        return new DxfDimensionExtractionResult(Array.Empty<DimensionData>(), warnings, error)
        {
            Document = document with { VerificationStatus = VerificationStatus.Error, VerificationError = error },
            Message = diagnosticMessage ?? error
        };
    }

    private AuditEntry CreateAudit(Document document, DocumentAuditAction action, long durationMilliseconds, int dimensionCount, IReadOnlyList<string>? warnings, string? error) =>
        new()
        {
            DocumentId = document.DocumentId,
            UserId = _session.CurrentUser?.UserId ?? _session.CurrentUser?.Username ?? "Anonymous",
            Action = action,
            DetailsJson = JsonSerializer.Serialize(new
            {
                durationMilliseconds,
                dimensionCount,
                warnings = warnings ?? Array.Empty<string>(),
                warningCount = warnings?.Count ?? 0,
                error
            })
        };

    private void LogWarnings(IReadOnlyList<string> warnings, Guid documentId, Guid versionId)
    {
        foreach (var warning in warnings)
            _logger?.Log(new InvalidDataException(warning), "DXF dimension extraction warning", $"documentId={documentId}; versionId={versionId}; warning={warning}");
    }

    private static string UserFailure(string detail) =>
        $"Dimension extraction failed: {(string.IsNullOrWhiteSpace(detail) ? "unable to parse DXF file." : detail)}";

    private static string FailureMessage(string operationStage, Exception exception) =>
        exception is IOException && exception.Message.Contains("configured maximum size", StringComparison.OrdinalIgnoreCase)
            ? "Dimension extraction failed: the DXF file exceeds the configured maximum size."
            : operationStage switch
    {
        "storage" => "Dimension extraction failed: unable to read the stored DXF file.",
        "parse" => "Dimension extraction failed: unable to parse DXF file.",
        "persistence" => "Dimension extraction failed: unable to save dimension extraction results.",
        _ => GenericFailureMessage
    };

    private static string Context(Document document, DocumentVersion version, string detail) =>
        $"documentId={document.DocumentId}; versionId={version.VersionId}; {detail}";

    private sealed class SizeLimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long _read;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.CanSeek ? Math.Min(inner.Length, maximumBytes) : _read;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= maximumBytes)
            {
                var probe = inner.ReadByte();
                if (probe >= 0) throw new IOException("The DXF file exceeds the configured maximum size.");
                return 0;
            }
            var allowed = (int)Math.Min(count, maximumBytes - _read);
            var read = inner.Read(buffer, offset, allowed);
            _read += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read >= maximumBytes)
            {
                var probe = new byte[1];
                var extra = await inner.ReadAsync(probe.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (extra > 0) throw new IOException("The DXF file exceeds the configured maximum size.");
                return 0;
            }
            var allowed = (int)Math.Min(buffer.Length, maximumBytes - _read);
            var read = await inner.ReadAsync(buffer[..allowed], cancellationToken).ConfigureAwait(false);
            _read += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { } base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
