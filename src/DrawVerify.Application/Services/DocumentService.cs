using System.Text.Json;
using DrawVerify.Application.StepUpload;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

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
    private readonly IAuditLogger _audit;
    private readonly IAuthenticationService _authentication;
    private readonly IStepUploadFileStore _files;
    private readonly StepUploadConfiguration _configuration;
    private readonly ICentralizedLogger? _logger;
    public bool CanUploadStep => _authentication.IsAuthenticated && _authentication.IsMaker;

    public DocumentService(IDocumentRepository documents, IAuditLogger audit, IAuthenticationService authentication, IStepUploadFileStore files, StepUploadConfiguration configuration, ICentralizedLogger? logger = null)
    {
        _documents = documents; _audit = audit; _authentication = authentication; _files = files; _configuration = configuration; _logger = logger; _configuration.Validate();
    }

    public async Task<StepUploadResult> PrepareStepUploadAsync(StepUploadRequest request)
    {
        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null) return validation;
        var duplicate = await _documents.FindByOriginalNameAndSizeAsync(Path.GetFileName(request.OriginalFileName), GetSize(request), request.CancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await WriteAuditAsync(duplicate.DocumentId, DocumentAuditAction.DuplicateDetected, request, "duplicate").ConfigureAwait(false);
            return new StepUploadResult { Kind = StepUploadResultKind.Duplicate, Message = DuplicateMessage, Document = duplicate, OriginalFileName = Path.GetFileName(request.OriginalFileName), FileSizeBytes = GetSize(request) };
        }
        return new StepUploadResult { Kind = StepUploadResultKind.Success, OriginalFileName = Path.GetFileName(request.OriginalFileName), FileSizeBytes = GetSize(request) };
    }

    public Task<StepUploadResult> UploadStepAsync(StepUploadRequest request, bool confirmDuplicate = false) => UploadStepCoreAsync(request, confirmDuplicate);

    public async Task<StepUploadResult> CancelDuplicateAsync(StepUploadRequest request, Guid documentId)
    {
        if (!_authentication.IsAuthenticated || !_authentication.IsMaker) return new() { Kind = StepUploadResultKind.Unauthorized, Message = "You must be an authenticated Maker to upload a STEP file." };
        await WriteAuditAsync(documentId, DocumentAuditAction.Cancelled, request, "duplicate cancelled").ConfigureAwait(false);
        return new StepUploadResult { Kind = StepUploadResultKind.Cancelled, Message = CancelledMessage, OriginalFileName = Path.GetFileName(request.OriginalFileName), FileSizeBytes = GetSize(request) };
    }

    private async Task<StepUploadResult> UploadStepCoreAsync(StepUploadRequest request, bool confirmDuplicate)
    {
        var validation = await ValidateAsync(request).ConfigureAwait(false);
        if (validation is not null) return validation;
        var name = Path.GetFileName(request.OriginalFileName);
        var size = GetSize(request);
        var duplicate = await _documents.FindByOriginalNameAndSizeAsync(name, size, request.CancellationToken).ConfigureAwait(false);
        if (duplicate is not null && !confirmDuplicate)
        {
            await WriteAuditAsync(duplicate.DocumentId, DocumentAuditAction.DuplicateDetected, request, "duplicate").ConfigureAwait(false);
            return new StepUploadResult { Kind = StepUploadResultKind.Duplicate, Message = DuplicateMessage, Document = duplicate, OriginalFileName = name, FileSizeBytes = size };
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
            var user = _authentication.CurrentUser!;
            if (duplicate is not null)
            {
                var version = NewVersion(duplicate.DocumentId, duplicate.CurrentVersion + 1, finalPath, size, user);
                var audit = CreateAudit(duplicate.DocumentId, DocumentAuditAction.Overwritten, request, "new version");
                if (_documents is ITransactionalDocumentRepository transactional)
                    await transactional.AddVersionWithAuditAsync(duplicate.DocumentId, version, audit, request.CancellationToken).ConfigureAwait(false);
                else
                {
                    await _documents.AddVersionAsync(duplicate.DocumentId, version, request.CancellationToken).ConfigureAwait(false);
                    await _audit.WriteAsync(audit, request.CancellationToken).ConfigureAwait(false);
                }
                return new StepUploadResult { Kind = StepUploadResultKind.Success, Message = $"Uploaded {name} ({size} bytes).", Document = duplicate, OriginalFileName = name, FileSizeBytes = size };
            }
            var document = NewDocument(name, size, finalPath, user);
            var uploadedAudit = CreateAudit(document.DocumentId, DocumentAuditAction.Uploaded, request, "uploaded");
            if (_documents is ITransactionalDocumentRepository createTransactional)
                await createTransactional.CreateWithVersionAndAuditAsync(document, document.Version!, uploadedAudit, request.CancellationToken).ConfigureAwait(false);
            else
            {
                await _documents.CreateWithVersionAsync(document, document.Version!, request.CancellationToken).ConfigureAwait(false);
                await _audit.WriteAsync(uploadedAudit, request.CancellationToken).ConfigureAwait(false);
            }
            return new StepUploadResult { Kind = StepUploadResultKind.Success, Message = $"Uploaded {name} ({size} bytes).", Document = document, OriginalFileName = name, FileSizeBytes = size };
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            if (staging is not null) await staging.DisposeAsync().ConfigureAwait(false);
            if (stagingPath is not null) await _files.DeleteAsync(stagingPath, CancellationToken.None).ConfigureAwait(false);
            await WriteAuditAsync(null, DocumentAuditAction.Cancelled, request, "cancelled", CancellationToken.None).ConfigureAwait(false);
            return new StepUploadResult { Kind = StepUploadResultKind.Cancelled, Message = CancelledMessage, OriginalFileName = name, FileSizeBytes = size };
        }
        catch (Exception exception)
        {
            if (staging is not null) await staging.DisposeAsync().ConfigureAwait(false);
            if (stagingPath is not null) await _files.DeleteAsync(stagingPath, CancellationToken.None).ConfigureAwait(false);
            if (finalPath is not null) await _files.DeleteAsync(finalPath).ConfigureAwait(false);
            _logger?.Log(exception, "STEP upload", $"fileName={name}; documentId={duplicate?.DocumentId.ToString() ?? "none"}");
            throw;
        }
    }

    private async Task<StepUploadResult?> ValidateAsync(StepUploadRequest request)
    {
        if (!_authentication.IsAuthenticated || !_authentication.IsMaker) return new() { Kind = StepUploadResultKind.Unauthorized, Message = "You must be an authenticated Maker to upload a STEP file." };
        var name = Path.GetFileName(request.OriginalFileName);
        if (!StepFileValidator.HasValidExtension(name)) return StepUploadResult.Rejected(TypeMessage);
        var size = GetSize(request);
        if (size > (long)_configuration.MaxFileSizeMb * 1024 * 1024) return StepUploadResult.Rejected($"File exceeds the maximum allowed size of {_configuration.MaxFileSizeMb} MB.");
        if (!await StepFileValidator.IsValidAsync(request.Content, request.CancellationToken).ConfigureAwait(false)) return StepUploadResult.Rejected(StepFileValidator.InvalidMessage);
        return null;
    }

    private static long GetSize(StepUploadRequest request) => request.FileSizeBytes ?? (request.Content.CanSeek ? request.Content.Length : throw new InvalidOperationException("A non-seekable upload stream must provide its byte size."));
    private static async Task CopyAsync(Stream source, Stream destination, long size, IProgress<long>? progress, CancellationToken token)
    { source.Position = 0; var buffer = new byte[81920]; long copied = 0; int read; while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); copied += read; progress?.Report(copied); } if (copied != size) throw new IOException("The upload stream changed while it was being copied."); await destination.FlushAsync(token).ConfigureAwait(false); }
    private static Document NewDocument(string name, long size, string path, User user) { var id = Guid.NewGuid(); var version = NewVersion(id, 1, path, size, user); return new Document { DocumentId = id, OriginalFileName = name, FileSizeBytes = size, UploadTimestampUtc = DateTime.UtcNow, UploaderUserId = user.UserId, UploaderDisplayName = user.DisplayName, Version = version, Versions = new[] { version } }; }
    private static DocumentVersion NewVersion(Guid id, int number, string path, long size, User user) => new() { DocumentId = id, VersionNumber = number, StoredRelativePath = path, FileSizeBytes = size, UploaderUserId = user.UserId, CreatedUtc = DateTime.UtcNow };
    private AuditEntry CreateAudit(Guid? id, DocumentAuditAction action, StepUploadRequest request, string detail) => new() { DocumentId = id, Action = action, UserId = _authentication.CurrentUser?.UserId ?? string.Empty, DetailsJson = JsonSerializer.Serialize(new { fileName = Path.GetFileName(request.OriginalFileName), detail }) };
    private async Task WriteAuditAsync(Guid? id, DocumentAuditAction action, StepUploadRequest request, string detail, CancellationToken? cancellationToken = null)
    {
        try { await _audit.WriteAsync(CreateAudit(id, action, request, detail), cancellationToken ?? request.CancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "STEP audit", $"action={action}; documentId={id?.ToString() ?? "none"}"); throw; }
    }
}
