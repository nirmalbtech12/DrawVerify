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

    public Task<IReadOnlyList<SharedDocument>> ListAsync(SharedDocumentQuery query, CancellationToken token = default)
        => _documents.ListAsync(RequireViewer().UserId, query, token);

    public async Task<SharedDocumentAccess> OpenAsync(Guid documentId, Guid? versionId = null, CancellationToken token = default)
    {
        var user = RequireViewer();
        var document = await _documents.GetAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException(UnsharedMessage);
        try
        {
            var content = document.DocumentType == DrawVerify.Core.Enums.DocumentType.DxfDrawing
                ? await _dxfFiles.OpenReadAsync(document.StoredRelativePath, token).ConfigureAwait(false)
                : await _stepFiles.OpenReadAsync(document.StoredRelativePath, token).ConfigureAwait(false);
            await WriteAuditAsync(document, user, DocumentAuditAction.DocumentViewed, token).ConfigureAwait(false);
            return new SharedDocumentAccess(document, content);
        }
        catch (Exception exception)
        {
            _logger.Log(exception, "Approved document view", $"Unable to open document {document.DocumentId}, version {document.VersionId}.");
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(Guid documentId, Guid? versionId = null, CancellationToken token = default)
    {
        if (!_configuration.AllowDownload) throw new UnauthorizedAccessException("Downloading approved documents is disabled for Viewers.");
        var access = await OpenWithoutAuditAsync(documentId, versionId, token).ConfigureAwait(false);
        try { await WriteAuditAsync(access.Document, RequireViewer(), DocumentAuditAction.DocumentDownloaded, token).ConfigureAwait(false); return access.Content; }
        catch { await access.Content.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<SharedDocumentAccess> OpenWithoutAuditAsync(Guid documentId, Guid? versionId, CancellationToken token)
    {
        var user = RequireViewer();
        var document = await _documents.GetAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false) ?? throw new UnauthorizedAccessException(UnsharedMessage);
        var content = document.DocumentType == DrawVerify.Core.Enums.DocumentType.DxfDrawing ? await _dxfFiles.OpenReadAsync(document.StoredRelativePath, token) : await _stepFiles.OpenReadAsync(document.StoredRelativePath, token);
        return new SharedDocumentAccess(document, content);
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
