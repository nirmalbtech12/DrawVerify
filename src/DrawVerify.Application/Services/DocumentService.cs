using System.Text.Json;
using DrawVerify.Application.StepUpload;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Authorization;

namespace DrawVerify.Application.Services;

/// <summary>
/// Document lifecycle orchestration (create, version, archive) via IDocumentRepository.
/// </summary>
public sealed class DocumentService
{
    public const string TypeMessage = "Only STEP files (.step, .stp) are allowed.";
    public const string DuplicateMessage = "A file with the same name and size already exists. Create a new version?";
    public const string CancelledMessage = "Upload cancelled.";
    private readonly IDocumentRepository _documents;
    private readonly ITransactionalDocumentRepository _transactionalDocuments;
    private readonly IAuditLogger _audit;
    private readonly IStepUploadFileStore _files;
    private readonly StepUploadConfiguration _configuration;
    private readonly ICentralizedLogger? _logger;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly IUserRepository? _users;
    private readonly IWorkflowRepository? _workflow;
    private readonly Dictionary<string, StepUploadPreparation> _preparedDuplicates = new(StringComparer.Ordinal);
    public bool CanUploadStep => _session.IsAuthenticated && _authorization.IsAllowed(_session.CurrentUser, RolePermission.Upload);

    public DocumentService(IDocumentRepository documents, IAuditLogger audit, IStepUploadFileStore files, StepUploadConfiguration configuration, ICentralizedLogger? logger = null, object? stepModelValidator = null)
        : this(documents, audit, files, configuration, new SessionAuthenticationService(), logger, new RoleAuthorization(), stepModelValidator) { }

    public DocumentService(IDocumentRepository documents, IAuditLogger audit, IStepUploadFileStore files, StepUploadConfiguration configuration, ISessionAuthenticationService? session, ICentralizedLogger? logger = null, RoleAuthorization? authorization = null, object? stepModelValidator = null, IUserRepository? users = null, IWorkflowRepository? workflow = null)
    {
        _documents = documents; _transactionalDocuments = documents as ITransactionalDocumentRepository ?? throw new ArgumentException("The document repository must support transactional document, version, and audit persistence.", nameof(documents)); _audit = audit; _files = files; _configuration = configuration; _logger = logger; _session = session ?? new SessionAuthenticationService(); _authorization = authorization ?? new RoleAuthorization(); _users = users; _workflow = workflow; _configuration.Validate();
    }

    public async Task<Document> GetAccessibleAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The document was not found.");
        if (!await CanAccessAsync(document, cancellationToken).ConfigureAwait(false)) throw new UnauthorizedAccessException("You are not authorized to access this document.");
        return document;
    }

    public async Task<Stream> OpenAccessibleReadAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document.Version?.VersionId != versionId) throw new InvalidOperationException("The requested document version is not available.");
        return await _files.OpenReadAsync(document.Version.StoredRelativePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CanAccessAsync(Document document, CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || document.Status != DocumentStatus.Active) return false;
        var user = _session.CurrentUser!;
        if (_workflow is null) throw new NotSupportedException("Document access evaluation requires workflow status persistence.");
        var state = await _workflow.GetStateAsync(document.DocumentId, cancellationToken).ConfigureAwait(false);
        var approved = state?.Status is WorkflowStatus.FinallyApproved or WorkflowStatus.Released;
        if (approved) return _authorization.IsAllowed(user, RolePermission.ReadApprovedDocuments) || _authorization.IsAllowed(user, RolePermission.FinalApproval) || _authorization.IsAllowed(user, RolePermission.Upload) || _authorization.IsAllowed(user, RolePermission.ReviewVerification);
        return user.HasRole(SystemRoles.Maker) || user.HasRole(SystemRoles.Checker) || user.HasRole(SystemRoles.Approver);
    }

    public async Task<StepUploadResult> PrepareStepUploadAsync(StepUploadRequest request)
    {
        await RefreshAuthorizationAsync(request.CancellationToken).ConfigureAwait(false);
        if (!CanUploadStep) return StepUploadResult.Rejected("The Maker role is required to upload files.");
        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null) return validation;
        var duplicate = await _documents.FindByOriginalNameAndSizeAsync(Path.GetFileName(request.OriginalFileName), GetSize(request), request.CancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await WriteAuditAsync(duplicate.DocumentId, DocumentAuditAction.DuplicateDetected, request, "duplicate").ConfigureAwait(false);
            var preparation = new StepUploadPreparation { DuplicateDocument = duplicate, OriginalFileName = Path.GetFileName(request.OriginalFileName), FileSizeBytes = GetSize(request) };
            lock (_preparedDuplicates) _preparedDuplicates[PreparationKey(preparation.OriginalFileName, preparation.FileSizeBytes)] = preparation;
            return new StepUploadResult { Kind = StepUploadResultKind.Duplicate, Message = DuplicateMessage, Document = duplicate, Preparation = preparation, OriginalFileName = preparation.OriginalFileName, FileSizeBytes = preparation.FileSizeBytes };
        }
        return new StepUploadResult { Kind = StepUploadResultKind.Success, OriginalFileName = Path.GetFileName(request.OriginalFileName), FileSizeBytes = GetSize(request) };
    }

    public Task<StepUploadResult> UploadStepAsync(StepUploadRequest request, bool confirmDuplicate = false) => UploadStepCoreAsync(request, confirmDuplicate);

    public async Task<StepUploadResult> CancelDuplicateAsync(StepUploadRequest request, Guid documentId)
    {
        await RefreshAuthorizationAsync(request.CancellationToken).ConfigureAwait(false);
        if (!CanUploadStep) return StepUploadResult.Rejected("The Maker role is required to cancel an upload.");
        var name = Path.GetFileName(request.OriginalFileName);
        var size = GetSize(request);
        lock (_preparedDuplicates)
        {
            if (!_preparedDuplicates.Remove(PreparationKey(name, size), out var preparation) || preparation.DuplicateDocument?.DocumentId != documentId)
                return StepUploadResult.Rejected("The duplicate confirmation has expired or is invalid.");
        }
        await WriteAuditAsync(documentId, DocumentAuditAction.Cancelled, request, "duplicate cancelled").ConfigureAwait(false);
        return new StepUploadResult { Kind = StepUploadResultKind.Cancelled, Message = CancelledMessage, OriginalFileName = Path.GetFileName(request.OriginalFileName), FileSizeBytes = GetSize(request) };
    }

    private async Task<StepUploadResult> UploadStepCoreAsync(StepUploadRequest request, bool confirmDuplicate)
    {
        await RefreshAuthorizationAsync(request.CancellationToken).ConfigureAwait(false);
        if (!CanUploadStep) return StepUploadResult.Rejected("The Maker role is required to upload files.");
        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null) return validation;
        var name = Path.GetFileName(request.OriginalFileName);
        var size = GetSize(request);
        var duplicate = await _documents.FindByOriginalNameAndSizeAsync(name, size, request.CancellationToken).ConfigureAwait(false);
        if (duplicate is not null && !confirmDuplicate)
        {
            await WriteAuditAsync(duplicate.DocumentId, DocumentAuditAction.DuplicateDetected, request, "duplicate").ConfigureAwait(false);
            var preparation = new StepUploadPreparation { DuplicateDocument = duplicate, OriginalFileName = name, FileSizeBytes = size };
            lock (_preparedDuplicates) _preparedDuplicates[PreparationKey(name, size)] = preparation;
            return new StepUploadResult { Kind = StepUploadResultKind.Duplicate, Message = DuplicateMessage, Document = duplicate, Preparation = preparation, OriginalFileName = name, FileSizeBytes = size };
        }
        if (duplicate is not null && confirmDuplicate)
        {
            lock (_preparedDuplicates)
            {
                if (!_preparedDuplicates.Remove(PreparationKey(name, size), out var preparation) || preparation.DuplicateDocument?.DocumentId != duplicate.DocumentId)
                    return StepUploadResult.Rejected("The duplicate confirmation has expired or is invalid.");
            }
        }

        StepUploadStagingFile? staging = null;
        string? stagingPath = null;
        string? finalPath = null;
        try
        {
            staging = await _files.CreateStagingFileAsync(name, request.CancellationToken).ConfigureAwait(false);
            stagingPath = staging.RelativePath;
            await CopyAsync(request.Content, staging.Stream, size, request.Progress, request.CancellationToken).ConfigureAwait(false);
            var completedStaging = staging;
            await completedStaging.DisposeAsync().ConfigureAwait(false);
            finalPath = await _files.MoveToFinalAsync(completedStaging, request.CancellationToken).ConfigureAwait(false);
            staging = null;
            if (duplicate is not null)
            {
                var version = NewVersion(duplicate.DocumentId, duplicate.CurrentVersion + 1, finalPath, size);
                var audit = CreateAudit(duplicate.DocumentId, DocumentAuditAction.Overwritten, request, "new version", version);
                await _transactionalDocuments.AddVersionWithAuditAsync(duplicate.DocumentId, version, audit, request.CancellationToken).ConfigureAwait(false);
                return new StepUploadResult { Kind = StepUploadResultKind.Success, Message = $"Uploaded {name} ({size} bytes).", Document = duplicate, OriginalFileName = name, FileSizeBytes = size };
            }
            var document = NewDocument(name, size, finalPath);
            var uploadedAudit = CreateAudit(document.DocumentId, DocumentAuditAction.Uploaded, request, "uploaded", document.Version);
            await _transactionalDocuments.CreateWithVersionAndAuditAsync(document, document.Version!, uploadedAudit, request.CancellationToken).ConfigureAwait(false);
            return new StepUploadResult { Kind = StepUploadResultKind.Success, Message = $"Uploaded {name} ({size} bytes).", Document = document, OriginalFileName = name, FileSizeBytes = size };
        }
        catch (OperationCanceledException cancellationException) when (request.CancellationToken.IsCancellationRequested)
        {
            var cleanupSucceeded = await TryDisposeAsync(staging, cancellationException, "cancellation").ConfigureAwait(false);
            var stagingDeleted = await TryDeleteAsync(stagingPath, cancellationException, "cancellation").ConfigureAwait(false);
            cleanupSucceeded &= stagingDeleted;
            cleanupSucceeded &= await TryDeleteAsync(finalPath, cancellationException, "cancellation").ConfigureAwait(false);
            if (!cleanupSucceeded) throw new IOException("Upload cancellation cleanup failed; the operation was not reported as cancelled.", cancellationException);
            await WriteAuditAsync(null, DocumentAuditAction.Cancelled, request, "cancelled", CancellationToken.None).ConfigureAwait(false);
            return new StepUploadResult { Kind = StepUploadResultKind.Cancelled, Message = CancelledMessage, OriginalFileName = name, FileSizeBytes = size };
        }
        catch (Exception exception)
        {
            await TryDisposeAsync(staging, exception, "failure compensation").ConfigureAwait(false);
            await TryDeleteAsync(stagingPath, exception, "failure compensation").ConfigureAwait(false);
            await TryDeleteAsync(finalPath, exception, "failure compensation").ConfigureAwait(false);
            _logger?.Log(exception, "STEP upload", $"fileName={name}; documentId={duplicate?.DocumentId.ToString() ?? "none"}");
            throw;
        }
    }

    private async Task RefreshAuthorizationAsync(CancellationToken cancellationToken)
    {
        var current = _session.CurrentUser;
        if (current is null || _users is null) return;
        try
        {
            var account = await _users.FindByUsernameAsync(current.Username, cancellationToken).ConfigureAwait(false);
            if (account is null) throw new UnauthorizedAccessException("The authenticated user no longer exists.");
            _session.ReplaceCurrentUserRoles(current.Username, account.Roles, account.RoleVersion);
        }
        catch (Exception ex)
        {
            _logger?.Log(ex, "Authorization refresh", $"Unable to refresh persisted roles for '{current.Username}'.");
            throw;
        }
    }

    private async Task<StepUploadResult?> ValidateAsync(StepUploadRequest request)
    {
        var name = Path.GetFileName(request.OriginalFileName);
        if (!StepFileValidator.HasValidExtension(name)) return StepUploadResult.Rejected(TypeMessage);
        var size = GetSize(request);
        if (!StepFileValidator.IsWithinSizeLimit(size, _configuration.MaxFileSizeMb)) return StepUploadResult.Rejected($"File exceeds the maximum allowed size of {_configuration.MaxFileSizeMb} MB.");
        StepFileValidationResult validation;
        try { validation = await StepFileValidator.ValidateAsync(request.Content, size, _configuration.ValidationTimeoutSeconds, request.CancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "STEP upload validation", $"fileName={name}; fileSizeBytes={size}"); throw; }
        if (!validation.IsValid)
        {
            _logger?.Log(new InvalidDataException(validation.Message), "STEP upload validation", $"fileName={name}; fileSizeBytes={size}; reason={validation.Reason}");
            await WriteAuditAsync(null, DocumentAuditAction.InvalidStepFileRejected, request, validation.Reason).ConfigureAwait(false);
            return StepUploadResult.Rejected(validation.Message);
        }
        return null;
    }

    private static long GetSize(StepUploadRequest request) => request.FileSizeBytes ?? (request.Content.CanSeek ? request.Content.Length : throw new InvalidOperationException("A non-seekable upload stream must provide its byte size."));
    private static string PreparationKey(string name, long size) => $"{name}\u001f{size}";
    private static async Task CopyAsync(Stream source, Stream destination, long size, IProgress<long>? progress, CancellationToken token)
    { source.Position = 0; var buffer = new byte[81920]; long copied = 0; int read; while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); copied += read; progress?.Report(copied); } if (copied != size) throw new IOException("The upload stream changed while it was being copied."); await destination.FlushAsync(token).ConfigureAwait(false); }
    private static Document NewDocument(string name, long size, string path) { var id = Guid.NewGuid(); var version = NewVersion(id, 1, path, size); return new Document { DocumentId = id, OriginalFileName = name, FileSizeBytes = size, UploadTimestampUtc = DateTime.UtcNow, Version = version, Versions = new[] { version } }; }
    private static DocumentVersion NewVersion(Guid id, int number, string path, long size) => new() { DocumentId = id, VersionNumber = number, StoredRelativePath = path, FileSizeBytes = size, CreatedUtc = DateTime.UtcNow };
    private AuditEntry CreateAudit(Guid? id, DocumentAuditAction action, StepUploadRequest request, string detail, DocumentVersion? version = null)
    {
        var user = _session.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated actor is required for document auditing.");
        if (id.HasValue && version is null && action is DocumentAuditAction.Uploaded or DocumentAuditAction.Overwritten) throw new InvalidOperationException("The affected document version is required for lifecycle auditing.");
        var name = Path.GetFileName(request.OriginalFileName);
        var details = action == DocumentAuditAction.InvalidStepFileRejected
            ? JsonSerializer.Serialize(new { originalFileName = name, fileSizeBytes = GetSize(request), reason = detail })
            : JsonSerializer.Serialize(new { fileName = name, detail });
        return new AuditEntry { DocumentId = id, Action = action, UserId = user.UserId, DocumentRevision = version?.VersionNumber.ToString(), DocumentVersion = version?.VersionId.ToString("D"), DetailsJson = details };
    }
    private async Task WriteAuditAsync(Guid? id, DocumentAuditAction action, StepUploadRequest request, string detail, CancellationToken? cancellationToken = null)
    {
        try { var version = id.HasValue ? (await _documents.GetAsync(id.Value, cancellationToken ?? request.CancellationToken).ConfigureAwait(false))?.Version : null; await _audit.WriteAsync(CreateAudit(id, action, request, detail, version), cancellationToken ?? request.CancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "STEP audit", $"action={action}; documentId={id?.ToString() ?? "none"}"); throw; }
    }
    private async Task<bool> TryDeleteAsync(string? relativePath, Exception originalException, string operation)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return true;
        try { await _files.DeleteAsync(relativePath, CancellationToken.None).ConfigureAwait(false); return true; }
        catch (Exception cleanupException) { _logger?.Log(cleanupException, "STEP upload cleanup", $"operation={operation}; orphanPath={relativePath}; original={originalException.GetType().Name}: {originalException.Message}"); return false; }
    }
    private async Task<bool> TryDisposeAsync(StepUploadStagingFile? staging, Exception originalException, string operation)
    {
        if (staging is null) return true;
        try { await staging.DisposeAsync().ConfigureAwait(false); return true; }
        catch (Exception cleanupException) { _logger?.Log(cleanupException, "STEP upload cleanup", $"operation={operation}; stagingPath={staging.RelativePath}; original={originalException.GetType().Name}: {originalException.Message}"); return false; }
    }
}
