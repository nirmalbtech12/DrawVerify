using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class ApprovedDocumentService : IApprovedDocumentService
{
    public const string UnsharedMessage = "You do not have permission to view this document";
    public const string RevokedMessage = "You no longer have permission to view this document";
    private readonly ISharedDocumentRepository _documents;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly IAuditLogger _audit;
    private readonly ICentralizedLogger _logger;
    private readonly IStepUploadFileStore _stepFiles;
    private readonly IDxfUploadFileStore _dxfFiles;
    private readonly ViewerDocumentConfiguration _configuration;

    public ApprovedDocumentService(ISharedDocumentRepository documents, ISessionAuthenticationService session, RoleAuthorization authorization, IAuditLogger audit, ICentralizedLogger logger, IStepUploadFileStore stepFiles, IDxfUploadFileStore dxfFiles, ViewerDocumentConfiguration? configuration = null)
        => (_documents, _session, _authorization, _audit, _logger, _stepFiles, _dxfFiles, _configuration) = (documents, session, authorization, audit, logger, stepFiles, dxfFiles, configuration ?? new());

    public bool AllowDownload => _configuration.AllowDownload;

    public async Task<IReadOnlyList<SharedDocument>> ListAsync(SharedDocumentQuery query, CancellationToken token = default)
    {
        var user = RequireViewer();
        try
        {
            return await _documents.ListAsync(user.UserId, query, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Log(exception, "List shared approved documents", $"Viewer '{user.UserId}' query='{query.NormalizedSearch ?? string.Empty}'.");
            throw;
        }
    }

    public async Task<SharedDocumentAccess> OpenAsync(Guid documentId, Guid? versionId = null, CancellationToken token = default)
    {
        var user = RequireViewer();
        var document = await _documents.GetAccessStateAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false);
        if (document is null) throw new UnauthorizedAccessException(UnsharedMessage);
        if (document.IsRevoked) throw new UnauthorizedAccessException(RevokedMessage);
        if (!string.IsNullOrWhiteSpace(document.ApprovalInformationError))
            _logger.Log(new InvalidDataException(document.ApprovalInformationError), "Approved document approval metadata", $"Invalid approval metadata for document {document.DocumentId}, version {document.VersionId}.");
        else if (document.ApproverSignature is null || document.ApprovalStamp is null)
            _logger.Log(new InvalidDataException("Final approval metadata is incomplete."), "Approved document approval metadata", $"Approval metadata is incomplete for document {document.DocumentId}, version {document.VersionId}.");
        Stream? content = null;
        try
        {
            content = document.DocumentType == DrawVerify.Core.Enums.DocumentType.DxfDrawing
                ? await _dxfFiles.OpenReadAsync(document.StoredRelativePath, token).ConfigureAwait(false)
                : await _stepFiles.OpenReadAsync(document.StoredRelativePath, token).ConfigureAwait(false);
            await WriteAuditAsync(document, user, DocumentAuditAction.DocumentViewed, token).ConfigureAwait(false);
            return new SharedDocumentAccess(document, content);
        }
        catch (Exception exception)
        {
            if (content is not null)
            {
                try { await content.DisposeAsync().ConfigureAwait(false); }
                catch (Exception disposeException) { _logger.Log(disposeException, "Approved document view cleanup", $"Unable to dispose the failed view stream for document {document.DocumentId}, version {document.VersionId}."); }
            }
            _logger.Log(exception, "Approved document view", $"Unable to open document {document.DocumentId}, version {document.VersionId}.");
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(Guid documentId, Guid? versionId = null, CancellationToken token = default)
    {
        if (!_configuration.AllowDownload) throw new UnauthorizedAccessException("Downloading approved documents is disabled for Viewers.");
        SharedDocumentAccess? access = null;
        try { access = await OpenWithoutAuditAsync(documentId, versionId, token).ConfigureAwait(false); await WriteAuditAsync(access.Document, RequireViewer(), DocumentAuditAction.DocumentDownloaded, token).ConfigureAwait(false); return access.Content; }
        catch (Exception exception)
        {
            _logger.Log(exception, "Approved document download", $"Unable to download document {documentId}, version {versionId?.ToString() ?? "latest"}, action={DocumentAuditAction.DocumentDownloaded}.");
            if (access?.Content is not null) await access.Content.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SharedDocumentAccess> OpenWithoutAuditAsync(Guid documentId, Guid? versionId, CancellationToken token)
    {
        var user = RequireViewer();
        var document = await _documents.GetAccessStateAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false);
        if (document is null) throw new UnauthorizedAccessException(UnsharedMessage);
        if (document.IsRevoked) throw new UnauthorizedAccessException(RevokedMessage);
        try
        {
            var content = document.DocumentType == DrawVerify.Core.Enums.DocumentType.DxfDrawing ? await _dxfFiles.OpenReadAsync(document.StoredRelativePath, token) : await _stepFiles.OpenReadAsync(document.StoredRelativePath, token);
            return new SharedDocumentAccess(document, content);
        }
        catch (Exception exception)
        {
            _logger.Log(exception, "Approved document download file resolution", $"Unable to resolve document {document.DocumentId}, version {document.VersionId}, action={DocumentAuditAction.DocumentDownloaded}.");
            throw;
        }
    }

    private async Task WriteAuditAsync(SharedDocument document, AuthenticatedUser user, DocumentAuditAction action, CancellationToken token)
    {
        var entry = new AuditEntry { DocumentId = document.DocumentId, UserId = user.UserId, ActorRole = SystemRoles.Viewer, DocumentRevision = document.RevisionText, DocumentVersion = document.VersionId.ToString("D"), Action = action, TimestampUtc = DateTime.UtcNow, DetailsJson = JsonSerializer.Serialize(new { documentId = document.DocumentId, versionId = document.VersionId, action = action.ToString() }) };
        await _audit.WriteAsync(entry, token).ConfigureAwait(false);
    }

    private AuthenticatedUser RequireViewer()
    {
        var user = _session.CurrentUser;
        if (user is null || !_authorization.IsAllowed(user, RolePermission.ReadApprovedDocuments) || !user.HasRole(SystemRoles.Viewer)) throw new UnauthorizedAccessException("Only an authenticated Viewer may access shared approved documents.");
        return user;
    }
}
