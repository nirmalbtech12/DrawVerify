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
    private readonly IViewerAccessRepository? _viewerAccess;
    private readonly IInternalSharedLinkRepository? _sharedLinks;

    public ApprovedDocumentService(ISharedDocumentRepository documents, ISessionAuthenticationService session, RoleAuthorization authorization, IAuditLogger audit, ICentralizedLogger logger, IStepUploadFileStore stepFiles, IDxfUploadFileStore dxfFiles, ViewerDocumentConfiguration? configuration = null, IViewerAccessRepository? viewerAccess = null, IInternalSharedLinkRepository? sharedLinks = null)
        => (_documents, _session, _authorization, _audit, _logger, _stepFiles, _dxfFiles, _configuration, _viewerAccess, _sharedLinks) = (documents, session, authorization, audit, logger, stepFiles, dxfFiles, configuration ?? new(), viewerAccess, sharedLinks);

    public bool AllowDownload => _configuration.AllowDownload;
    public async Task<SharedDocument?> GetSharedLinkDocumentAsync(Guid documentId, Guid versionId, CancellationToken token = default)
        => await _documents.GetAsync("__shared_link__", documentId, versionId, token).ConfigureAwait(false);

    public async Task<SharedDocumentAccess> OpenSharedLinkAsync(SharedLinkViewerContext context, CancellationToken token = default)
    {
        var user = _session.CurrentUser ?? throw new UnauthorizedAccessException("Authentication is required to access this shared link.");
        var document = await GetSharedLinkDocumentAsync(context.DocumentId, context.VersionId, token).ConfigureAwait(false) ?? throw new UnauthorizedAccessException(UnsharedMessage);
        if (_sharedLinks is not null)
        {
            var link = await _sharedLinks.GetByIdAsync(context.LinkId, token).ConfigureAwait(false);
            if (link is null || link.DocumentId != context.DocumentId || link.VersionId != context.VersionId || link.Status(DateTime.UtcNow) != SharedLinkStatus.Active) throw new UnauthorizedAccessException(RevokedMessage);
        }
        LogApprovalMetadata(document); EnsureSupportedFormat(document);
        var content = await OpenContentAsync(document, token).ConfigureAwait(false);
        await WriteAuditAsync(document, user, DocumentAuditAction.DocumentViewed, token).ConfigureAwait(false);
        return new SharedDocumentAccess(document, content);
    }

    public async Task<bool> ValidateSharedLinkAccessAsync(SharedLinkViewerContext context, CancellationToken token = default)
    {
        if (!_session.IsAuthenticated || _sharedLinks is null) return false;
        var link = await _sharedLinks.GetByIdAsync(context.LinkId, token).ConfigureAwait(false);
        return link is not null && link.DocumentId == context.DocumentId && link.VersionId == context.VersionId && link.Status(DateTime.UtcNow) == SharedLinkStatus.Active;
    }
    public async Task<bool> ValidateAccessAsync(Guid documentId, Guid versionId, CancellationToken token = default)
    {
        var user = RequireViewer();
        if (_viewerAccess is null) return true;
        try { return await _viewerAccess.HasAccessAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "Validate active viewer access", $"Document={documentId}, version={versionId}, user={user.UserId}."); throw; }
    }

    public async Task<IReadOnlyList<SharedDocument>> ListAsync(SharedDocumentQuery query, CancellationToken token = default)
    {
        var user = RequireViewer();
        try
        {
            var rows = await _documents.ListAsync(user.UserId, query, token).ConfigureAwait(false);
            if (_viewerAccess is null) return rows;
            var filtered = new List<SharedDocument>();
            foreach (var row in rows)
                if (await _viewerAccess.HasAccessAsync(user.UserId, row.DocumentId, row.VersionId, token).ConfigureAwait(false)) filtered.Add(row);
            return filtered;
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
        SharedDocument? document = null;
        Stream? content = null;
        try
        {
            document = await _documents.GetAccessStateAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false);
            if (document is null) throw new UnauthorizedAccessException(UnsharedMessage);
            if (_viewerAccess is not null && !await _viewerAccess.HasAccessAsync(user.UserId, document.DocumentId, document.VersionId, token).ConfigureAwait(false)) throw new UnauthorizedAccessException(RevokedMessage);
            if (document.IsRevoked) throw new UnauthorizedAccessException(RevokedMessage);
            LogApprovalMetadata(document);
            EnsureSupportedFormat(document);
            content = await OpenContentAsync(document, token).ConfigureAwait(false);
            await WriteAuditAsync(document, user, DocumentAuditAction.DocumentViewed, token).ConfigureAwait(false);
            return new SharedDocumentAccess(document, content!);
        }
        catch (Exception exception)
        {
            if (content is not null)
            {
                try { await content.DisposeAsync().ConfigureAwait(false); }
                catch (Exception disposeException) { _logger.Log(disposeException, "Approved document view cleanup", $"Unable to dispose the failed view stream for document {document?.DocumentId.ToString() ?? documentId.ToString()}, version {document?.VersionId.ToString() ?? versionId?.ToString() ?? "latest"}."); }
            }
            _logger.Log(exception, "Approved document view", $"Unable to open document {documentId}, version {versionId?.ToString() ?? "latest"}, format={document?.DocumentType.ToString() ?? "unknown"}.");
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
        SharedDocument? document = null;
        try
        {
            document = await _documents.GetAccessStateAsync(user.UserId, documentId, versionId, token).ConfigureAwait(false);
            if (document is null) throw new UnauthorizedAccessException(UnsharedMessage);
            if (_viewerAccess is not null && !await _viewerAccess.HasAccessAsync(user.UserId, document.DocumentId, document.VersionId, token).ConfigureAwait(false)) throw new UnauthorizedAccessException(RevokedMessage);
            if (document.IsRevoked) throw new UnauthorizedAccessException(RevokedMessage);
            EnsureSupportedFormat(document);
            var content = await OpenContentAsync(document, token).ConfigureAwait(false);
            return new SharedDocumentAccess(document, content);
        }
        catch (Exception exception)
        {
            _logger.Log(exception, "Approved document download resolution", $"Unable to resolve document {documentId}, version {versionId?.ToString() ?? "latest"}, format={document?.DocumentType.ToString() ?? "unknown"}, action={DocumentAuditAction.DocumentDownloaded}.");
            throw;
        }
    }

    private async Task<Stream> OpenContentAsync(SharedDocument document, CancellationToken token)
        => document.DocumentType switch
        {
            DrawVerify.Core.Enums.DocumentType.StepModel => await _stepFiles.OpenReadAsync(document.StoredRelativePath, token).ConfigureAwait(false),
            DrawVerify.Core.Enums.DocumentType.DxfDrawing => await _dxfFiles.OpenReadAsync(document.StoredRelativePath, token).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Document format '{document.DocumentType}' is not supported by the approved viewer.")
        };

    private static void EnsureSupportedFormat(SharedDocument document)
    {
        if (document.DocumentType is not (DrawVerify.Core.Enums.DocumentType.StepModel or DrawVerify.Core.Enums.DocumentType.DxfDrawing))
            throw new NotSupportedException($"Document format '{document.DocumentType}' is not supported by the approved viewer.");
    }

    private void LogApprovalMetadata(SharedDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.ApprovalInformationError))
            _logger.Log(new InvalidDataException(document.ApprovalInformationError), "Approved document approval metadata", $"Invalid approval metadata for document {document.DocumentId}, version {document.VersionId}, format={document.DocumentType}.");
        else if (document.ApproverSignature is null || document.ApprovalStamp is null)
            _logger.Log(new InvalidDataException("Final approval metadata is incomplete."), "Approved document approval metadata", $"Approval metadata is incomplete for document {document.DocumentId}, version {document.VersionId}, format={document.DocumentType}.");
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
