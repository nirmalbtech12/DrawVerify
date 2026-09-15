using System.Text.Json;
using DrawVerify.Application.DxfUpload;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class DxfDocumentService
{
    public const string TypeMessage = DxfFileValidator.TypeMessage;
    public const string DuplicateMessage = "A file with the same name and size already exists. Create a new version?";
    public const string CancelledMessage = "Upload cancelled.";

    private readonly IDocumentRepository _documents;
    private readonly ITransactionalDocumentRepository _transactionalDocuments;
    private readonly IAuditLogger _audit;
    private readonly IDxfUploadFileStore _files;
    private readonly DxfUploadConfiguration _configuration;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;
    private readonly IUserRepository? _users;
    private readonly Dictionary<string, DxfUploadPreparation> _preparedDuplicates = new(StringComparer.Ordinal);

    public DxfDocumentService(
        IDocumentRepository documents,
        IAuditLogger audit,
        IDxfUploadFileStore files,
        DxfUploadConfiguration configuration,
        ICentralizedLogger? logger = null)
        : this(documents, audit, files, configuration, new SessionAuthenticationService(), logger, new RoleAuthorization())
    {
    }

    public DxfDocumentService(
        IDocumentRepository documents,
        IAuditLogger audit,
        IDxfUploadFileStore files,
        DxfUploadConfiguration configuration,
        ISessionAuthenticationService? session,
        ICentralizedLogger? logger = null,
        RoleAuthorization? authorization = null,
        IUserRepository? users = null)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _transactionalDocuments = documents as ITransactionalDocumentRepository
            ?? throw new ArgumentException("The document repository must support transactional document, version, and audit persistence.", nameof(documents));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _session = session ?? new SessionAuthenticationService();
        _logger = logger;
        _authorization = authorization ?? new RoleAuthorization();
        _users = users;
        _configuration.Validate();
    }

    public bool CanUploadDxf => _session.IsAuthenticated &&
        _authorization.IsAllowed(_session.CurrentUser, RolePermission.Upload);

    public async Task<DxfUploadResult> PrepareDxfUploadAsync(DxfUploadRequest request)
    {
        await RefreshAuthorizationForUploadAsync(request).ConfigureAwait(false);
        if (!CanUploadDxf)
            return UnauthorizedResult("The Maker role is required to upload files.");

        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null)
            return validation;

        var name = LeafName(request.OriginalFileName);
        var size = GetSize(request);
        var duplicate = await FindDuplicateAsync(name, size, request).ConfigureAwait(false);
        if (duplicate is null)
            return new DxfUploadResult { Kind = DxfUploadResultKind.Success, OriginalFileName = name, FileSizeBytes = size };

        await WriteAuditAsync(duplicate.DocumentId, DocumentAuditAction.DuplicateDetected, request, "duplicate").ConfigureAwait(false);
        var preparation = new DxfUploadPreparation { DuplicateDocument = duplicate, OriginalFileName = name, FileSizeBytes = size };
        lock (_preparedDuplicates)
            _preparedDuplicates[PreparationKey(name, size)] = preparation;
        return DuplicateResult(preparation);
    }

    public Task<DxfUploadResult> UploadDxfAsync(DxfUploadRequest request, bool confirmDuplicate = false) =>
        UploadDxfCoreAsync(request, confirmDuplicate);

    public Task<DxfUploadResult> UploadDxfAsync(DxfUploadRequest request, DxfUploadPreparation preparation) =>
        UploadDxfCoreAsync(request, true, preparation);

    public async Task<DxfUploadResult> ReplaceDxfAsync(Guid documentId, DxfUploadRequest request)
    {
        await RefreshAuthorizationForUploadAsync(request).ConfigureAwait(false);
        if (!CanUploadDxf) return UnauthorizedResult("The Maker role is required to replace files.");
        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null) return validation;
        var existing = await _documents.GetAsync(documentId, request.CancellationToken).ConfigureAwait(false);
        if (existing is null || existing.DocumentType != DrawVerify.Core.Enums.DocumentType.DxfDrawing)
            return DxfUploadResult.Rejected("The selected DXF document was not found or is not replaceable.");
        DxfUploadStagingFile? staging = null; string? stagingPath = null; string? finalPath = null;
        try
        {
            var name = LeafName(request.OriginalFileName); var size = GetSize(request);
            staging = await _files.CreateStagingFileAsync(name, request.CancellationToken).ConfigureAwait(false); stagingPath = staging.RelativePath;
            await CopyAsync(request.Content, staging.Stream, size, request.Progress, request.CancellationToken).ConfigureAwait(false);
            await staging.DisposeAsync().ConfigureAwait(false);
            finalPath = await _files.MoveToFinalAsync(staging, request.CancellationToken).ConfigureAwait(false); staging = null;
            var current = _documents.GetCurrentVersion(documentId) ?? existing.Version ?? throw new InvalidOperationException("The current DXF version was not found.");
            var version = NewVersion(documentId, Math.Max(existing.CurrentVersion, current.VersionNumber) + 1, finalPath, size);
            var audit = CreateAudit(documentId, DocumentAuditAction.DxfReplaced, request, JsonSerializer.Serialize(new { replacedVersionId = current.VersionId, newVersionId = version.VersionId }), version);
            await _transactionalDocuments.AddVersionWithAuditAsync(documentId, version, audit, request.CancellationToken).ConfigureAwait(false);
            var updated = existing with { FileSizeBytes = size, UploadTimestampUtc = version.CreatedUtc, CurrentVersion = version.VersionNumber, VerificationStatus = VerificationStatus.PendingVerification, VerificationError = null, DrawingSource = DrawingSource.Uploaded, Version = version, Versions = existing.Versions.Append(version).ToArray() };
            return SuccessResult(updated, name, size);
        }
        catch (Exception exception)
        {
            await TryDisposeAsync(staging, exception, "replacement failure compensation").ConfigureAwait(false);
            await TryDeleteAsync(stagingPath, exception, "replacement failure compensation").ConfigureAwait(false);
            await TryDeleteAsync(finalPath, exception, "replacement failure compensation").ConfigureAwait(false);
            _logger?.Log(exception, "DXF replacement", $"documentId={documentId}");
            throw;
        }
    }

    public async Task<DxfUploadResult> CancelDuplicateAsync(DxfUploadRequest request, Guid documentId)
    {
        await RefreshAuthorizationForUploadAsync(request).ConfigureAwait(false);
        if (!CanUploadDxf)
            return UnauthorizedResult("The Maker role is required to cancel an upload.");

        var name = LeafName(request.OriginalFileName);
        var size = GetSize(request);
        lock (_preparedDuplicates)
        {
            if (!_preparedDuplicates.Remove(PreparationKey(name, size), out var preparation) ||
                preparation.DuplicateDocument?.DocumentId != documentId)
                return DxfUploadResult.Rejected("The duplicate confirmation has expired or is invalid.");
        }

        await WriteAuditAsync(null, DocumentAuditAction.Cancelled, request, "duplicate cancelled").ConfigureAwait(false);
        return new DxfUploadResult { Kind = DxfUploadResultKind.Cancelled, Message = CancelledMessage, OriginalFileName = name, FileSizeBytes = size };
    }

    private async Task<DxfUploadResult> UploadDxfCoreAsync(
        DxfUploadRequest request,
        bool confirmDuplicate,
        DxfUploadPreparation? suppliedPreparation = null)
    {
        await RefreshAuthorizationForUploadAsync(request).ConfigureAwait(false);
        if (!CanUploadDxf)
            return UnauthorizedResult("The Maker role is required to upload files.");

        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null)
            return validation;

        var name = LeafName(request.OriginalFileName);
        var size = GetSize(request);
        var duplicate = await FindDuplicateAsync(name, size, request).ConfigureAwait(false);
        if (duplicate is not null && !confirmDuplicate)
        {
            await WriteAuditAsync(duplicate.DocumentId, DocumentAuditAction.DuplicateDetected, request, "duplicate").ConfigureAwait(false);
            var preparation = new DxfUploadPreparation { DuplicateDocument = duplicate, OriginalFileName = name, FileSizeBytes = size };
            lock (_preparedDuplicates)
                _preparedDuplicates[PreparationKey(name, size)] = preparation;
            return DuplicateResult(preparation);
        }

        if (duplicate is not null && confirmDuplicate)
        {
            lock (_preparedDuplicates)
            {
                if (!_preparedDuplicates.Remove(PreparationKey(name, size), out var preparation) ||
                    preparation.DuplicateDocument?.DocumentId != duplicate.DocumentId ||
                    (suppliedPreparation is not null && preparation.ConfirmationToken != suppliedPreparation.ConfirmationToken))
                    return DxfUploadResult.Rejected("The duplicate confirmation has expired or is invalid.");
            }
        }

        DxfUploadStagingFile? staging = null;
        string? stagingPath = null;
        string? finalPath = null;
        try
        {
            staging = await _files.CreateStagingFileAsync(name, request.CancellationToken).ConfigureAwait(false);
            stagingPath = staging.RelativePath;
            await CopyAsync(request.Content, staging.Stream, size, request.Progress, request.CancellationToken).ConfigureAwait(false);
            await staging.DisposeAsync().ConfigureAwait(false);
            finalPath = await _files.MoveToFinalAsync(staging, request.CancellationToken).ConfigureAwait(false);
            staging = null;

            if (duplicate is not null)
            {
                var version = NewVersion(duplicate.DocumentId, duplicate.CurrentVersion + 1, finalPath, size);
                var audit = CreateAudit(duplicate.DocumentId, DocumentAuditAction.Overwritten, request, "new version", version);
                await _transactionalDocuments.AddVersionWithAuditAsync(duplicate.DocumentId, version, audit, request.CancellationToken).ConfigureAwait(false);
                var updated = duplicate with
                {
                    FileSizeBytes = size,
                    UploadTimestampUtc = version.CreatedUtc,
                    CurrentVersion = version.VersionNumber,
                    Version = version,
                    Versions = duplicate.Versions.Append(version).ToArray()
                };
                return SuccessResult(updated, name, size);
            }

            var document = NewDocument(name, size, finalPath);
            var uploadedAudit = CreateAudit(document.DocumentId, DocumentAuditAction.Uploaded, request, "uploaded", document.Version);
            await _transactionalDocuments.CreateWithVersionAndAuditAsync(document, document.Version!, uploadedAudit, request.CancellationToken).ConfigureAwait(false);
            return SuccessResult(document, name, size);
        }
        catch (OperationCanceledException cancellationException) when (request.CancellationToken.IsCancellationRequested)
        {
            var cleanupSucceeded = await TryDisposeAsync(staging, cancellationException, "cancellation").ConfigureAwait(false);
            cleanupSucceeded &= await TryDeleteAsync(stagingPath, cancellationException, "cancellation").ConfigureAwait(false);
            cleanupSucceeded &= await TryDeleteAsync(finalPath, cancellationException, "cancellation").ConfigureAwait(false);
            if (!cleanupSucceeded)
                throw new IOException("Upload cancellation cleanup failed; the operation was not reported as cancelled.", cancellationException);

            await WriteAuditAsync(null, DocumentAuditAction.Cancelled, request, "cancelled", CancellationToken.None).ConfigureAwait(false);
            return new DxfUploadResult { Kind = DxfUploadResultKind.Cancelled, Message = CancelledMessage, OriginalFileName = name, FileSizeBytes = size };
        }
        catch (Exception exception)
        {
            await TryDisposeAsync(staging, exception, "failure compensation").ConfigureAwait(false);
            await TryDeleteAsync(stagingPath, exception, "failure compensation").ConfigureAwait(false);
            await TryDeleteAsync(finalPath, exception, "failure compensation").ConfigureAwait(false);
            _logger?.Log(exception, "DXF upload", $"fileName={name}; fileSizeBytes={size}; documentId={duplicate?.DocumentId.ToString() ?? "none"}");
            throw;
        }
    }

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
                concreteSession.SignIn(new AuthenticatedUser(account.Username, account.Roles, account.RoleVersion) { UserId = account.Id.ToString(), DisplayName = account.DisplayName });
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF authorization refresh", $"Unable to refresh persisted roles for '{current.Username}'.");
            throw;
        }
    }

    private async Task RefreshAuthorizationForUploadAsync(DxfUploadRequest request)
    {
        try
        {
            await RefreshAuthorizationAsync(request.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            await WriteCancellationAuditAsync(request, "cancelled during upload preparation", GetSafeSize(request)).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<DxfUploadResult?> ValidateAsync(DxfUploadRequest request)
    {
        var name = LeafName(request.OriginalFileName);
        if (!DxfFileValidator.HasValidExtension(name))
            return DxfUploadResult.Rejected(TypeMessage);

        long size = 0;
        try
        {
            size = GetSize(request);
            if (!DxfFileValidator.IsWithinSizeLimit(size, _configuration.MaxFileSizeMb))
                return DxfUploadResult.Rejected(string.Format(DxfFileValidator.SizeMessageFormat, _configuration.MaxFileSizeMb));

            var validation = await DxfFileValidator.ValidateAsync(request.Content, size, _configuration.ValidationTimeoutSeconds, request.CancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                _logger?.Log(new InvalidDataException(validation.Message), "DXF upload validation", $"fileName={name}; fileSizeBytes={size}; reason={validation.Reason}");
                await WriteAuditAsync(null, DocumentAuditAction.InvalidDxfFileRejected, request, validation.Reason).ConfigureAwait(false);
                return DxfUploadResult.Rejected(validation.Message);
            }

            return null;
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && request.CancellationToken.IsCancellationRequested)
                await WriteCancellationAuditAsync(request, "cancelled during validation", size).ConfigureAwait(false);
            _logger?.Log(exception, "DXF upload validation", $"fileName={name}; fileSizeBytes={size}");
            throw;
        }
    }

    private async Task<Document?> FindDuplicateAsync(string name, long size, DxfUploadRequest request)
    {
        try
        {
            return await _documents.FindByOriginalNameAndSizeAsync(name, size, request.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && request.CancellationToken.IsCancellationRequested)
                await WriteCancellationAuditAsync(request, "cancelled during duplicate lookup", size).ConfigureAwait(false);
            _logger?.Log(exception, "DXF duplicate lookup", $"fileName={name}; fileSizeBytes={size}");
            throw;
        }
    }

    private static string LeafName(string originalFileName) => Path.GetFileName(originalFileName);

    private static long GetSize(DxfUploadRequest request) => request.FileSizeBytes ??
        (request.Content.CanSeek ? request.Content.Length : throw new InvalidOperationException("A non-seekable upload stream must provide its byte size."));

    private static string PreparationKey(string name, long size) => $"{name}\u001f{size}";

    private static async Task CopyAsync(Stream source, Stream destination, long size, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (!source.CanRead)
            throw new InvalidOperationException("The DXF upload stream is not readable.");
        if (source.CanSeek)
            source.Position = 0;

        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            progress?.Report(copied);
        }

        if (copied != size)
            throw new IOException("The upload stream changed while it was being copied.");
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private Document NewDocument(string name, long size, string path)
    {
        var current = _session.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated uploader is required.");
        if (string.IsNullOrWhiteSpace(current.UserId) || string.IsNullOrWhiteSpace(current.DisplayName))
            throw new InvalidOperationException("The authenticated uploader identity is incomplete.");
        var id = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var version = NewVersion(id, 1, path, size, timestamp);
        return new Document
        {
            DocumentId = id,
            OriginalFileName = name,
            FileSizeBytes = size,
            UploadTimestampUtc = timestamp,
            DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing,
            UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded,
            VerificationStatus = DrawVerify.Core.Enums.VerificationStatus.PendingVerification,
            DrawingSource = DrawingSource.Uploaded,
            UploaderUserId = current.UserId,
            UploaderDisplayName = current.DisplayName,
            Version = version,
            Versions = new[] { version }
        };
    }

    private static DocumentVersion NewVersion(Guid documentId, int number, string path, long size, DateTime? timestamp = null) =>
        new() { DocumentId = documentId, VersionNumber = number, StoredRelativePath = path, FileSizeBytes = size, CreatedUtc = timestamp ?? DateTime.UtcNow, DrawingSource = DrawingSource.Uploaded };

    private AuditEntry CreateAudit(Guid? documentId, DocumentAuditAction action, DxfUploadRequest request, string detail, DocumentVersion? version = null)
        => CreateAudit(documentId, action, request, detail, GetSize(request), version);

    private AuditEntry CreateAudit(Guid? documentId, DocumentAuditAction action, DxfUploadRequest request, string detail, long fileSizeBytes, DocumentVersion? version = null)
    {
        var current = _session.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated uploader is required.");
        if (documentId.HasValue && version is null && action is DocumentAuditAction.Uploaded or DocumentAuditAction.Overwritten or DocumentAuditAction.DxfReplaced) throw new InvalidOperationException("The affected document version is required for DXF lifecycle auditing.");
        var name = LeafName(request.OriginalFileName);
        var details = JsonSerializer.Serialize(new
        {
            originalFileName = name,
            fileSizeBytes,
            detail,
            uploaderUserId = current.UserId
        });
        return new AuditEntry
        {
            DocumentId = documentId, DocumentRevision = version?.VersionNumber.ToString(), DocumentVersion = version?.VersionId.ToString("D"),
            Action = action,
            UserId = current.UserId,
            DetailsJson = details
        };
    }

    private async Task WriteAuditAsync(Guid? documentId, DocumentAuditAction action, DxfUploadRequest request, string detail, CancellationToken? cancellationToken = null)
    {
        try
        {
            var version = documentId.HasValue ? (await _documents.GetAsync(documentId.Value, cancellationToken ?? request.CancellationToken).ConfigureAwait(false))?.Version : null;
            await _audit.WriteAsync(CreateAudit(documentId, action, request, detail, GetSize(request), version), cancellationToken ?? request.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && request.CancellationToken.IsCancellationRequested && action != DocumentAuditAction.Cancelled)
                await WriteCancellationAuditAsync(request, $"cancelled while recording {action}", GetSafeSize(request)).ConfigureAwait(false);
            _logger?.Log(exception, "DXF audit", $"action={action}; documentId={documentId?.ToString() ?? "none"}");
            throw;
        }
    }

    private async Task WriteCancellationAuditAsync(DxfUploadRequest request, string detail, long size)
    {
        if (!CanUploadDxf)
            return;

        try
        {
            await _audit.WriteAsync(CreateAudit(null, DocumentAuditAction.Cancelled, request, detail, size), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "DXF cancellation audit", $"fileName={LeafName(request.OriginalFileName)}; fileSizeBytes={size}");
            throw;
        }
    }

    private static long GetSafeSize(DxfUploadRequest request)
    {
        try { return GetSize(request); }
        catch { return 0; }
    }

    private async Task<bool> TryDeleteAsync(string? relativePath, Exception originalException, string operation)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return true;
        try
        {
            await _files.DeleteAsync(relativePath, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception cleanupException)
        {
            _logger?.Log(cleanupException, "DXF upload cleanup", $"operation={operation}; orphanPath={relativePath}; original={originalException.GetType().Name}: {originalException.Message}");
            return false;
        }
    }

    private async Task<bool> TryDisposeAsync(DxfUploadStagingFile? staging, Exception originalException, string operation)
    {
        if (staging is null)
            return true;
        try
        {
            await staging.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception cleanupException)
        {
            _logger?.Log(cleanupException, "DXF upload cleanup", $"operation={operation}; stagingPath={staging.RelativePath}; original={originalException.GetType().Name}: {originalException.Message}");
            return false;
        }
    }

    private static DxfUploadResult DuplicateResult(DxfUploadPreparation preparation) => new()
    {
        Kind = DxfUploadResultKind.Duplicate,
        Message = DuplicateMessage,
        Document = preparation.DuplicateDocument,
        Preparation = preparation,
        OriginalFileName = preparation.OriginalFileName,
        FileSizeBytes = preparation.FileSizeBytes
    };

    private static DxfUploadResult UnauthorizedResult(string message) => new()
    {
        Kind = DxfUploadResultKind.Unauthorized,
        Message = message
    };

    private static DxfUploadResult SuccessResult(Document document, string name, long size) => new()
    {
        Kind = DxfUploadResultKind.Success,
        Message = $"Uploaded {name} ({size} bytes).",
        Document = document,
        OriginalFileName = name,
        FileSizeBytes = size
    };
}
