using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class ViewerAccessService : IViewerAccessService
{
    public const string ApprovedOnlyMessage = "Only approved documents can be shared with Viewers.";
    private readonly IViewerAccessRepository _repository;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger _logger;
    private readonly ViewerAccessConfiguration _configuration;

    public ViewerAccessService(IViewerAccessRepository repository, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger logger, ViewerAccessConfiguration? configuration = null)
        => (_repository, _session, _authorization, _logger, _configuration) = (repository, session, authorization, logger, configuration ?? new());

    public bool CanManageAccess => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ControlDocumentAccess);

    public async Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        try { return await _repository.SearchTargetsAsync(search ?? string.Empty, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "Search viewer access targets", $"Search='{search ?? string.Empty}'."); throw; }
    }

    public async Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        RequireManager();
        try { return await _repository.ListGrantsAsync(documentId, versionId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "List viewer access grants", $"Document={documentId}, version={versionId}."); throw; }
    }

    public async Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, CancellationToken cancellationToken = default)
    {
        var actor = RequireManager();
        ValidateExpiration(change.ExpiresUtc);
        try { return await _repository.GrantAsync(documentId, versionId, change, actor, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "Grant viewer access", $"Document={documentId}, version={versionId}, actor={actor.UserId}, targets={change.Targets.Count}."); throw; }
    }

    public async Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, CancellationToken cancellationToken = default)
    {
        var actor = RequireManager();
        if (targets is null || targets.Count == 0) throw new ArgumentException("Select at least one Viewer or group to revoke.", nameof(targets));
        try { return await _repository.RevokeAsync(documentId, versionId, targets, actor, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "Revoke viewer access", $"Document={documentId}, version={versionId}, actor={actor.UserId}, targets={targets.Count}."); throw; }
    }

    public async Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        try { return await _repository.HasAccessAsync(userId, documentId, versionId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "Check viewer access", $"Document={documentId}, version={versionId}, user={userId}."); throw; }
    }

    public async Task<int> GetActiveShareCountAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        RequireManager();
        try { return (await _repository.ListGrantsAsync(documentId, versionId, cancellationToken).ConfigureAwait(false)).Count(x => x.IsEffective(DateTime.UtcNow)); }
        catch (Exception exception) { _logger.Log(exception, "Aggregate viewer sharing status", $"Document={documentId}, version={versionId}."); throw; }
    }

    public Task<ViewerAccessBulkPreview> PreviewBulkAsync(ViewerAccessBulkRequest request, CancellationToken cancellationToken = default)
    {
        RequireManager();
        if (!_configuration.AllowBulkActions) throw new InvalidOperationException("Bulk Viewer access actions are disabled.");
        if (request.Documents.Count == 0 || request.Targets.Count == 0) throw new ArgumentException("Select at least one document and one access target.");
        ValidateExpiration(request.ExpiresUtc);
        return Task.FromResult(new ViewerAccessBulkPreview(request.Documents.Count, request.Targets.Count, $"{(request.Revoke ? "Revoke" : "Grant")} access for {request.Targets.Count} target(s) across {request.Documents.Count} document version(s).", Array.Empty<string>()));
    }

    public async Task<ViewerAccessMutationResult> ConfirmBulkAsync(ViewerAccessBulkRequest request, CancellationToken cancellationToken = default)
    {
        await PreviewBulkAsync(request, cancellationToken).ConfigureAwait(false);
        var actor = RequireManager();
        try { return await _repository.MutateBulkAsync(request, actor, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger.Log(exception, "Bulk viewer access mutation", $"Documents={request.Documents.Count}, targets={request.Targets.Count}, actor={actor.UserId}, revoke={request.Revoke}."); throw; }
    }

    private AuthenticatedUser RequireManager()
    {
        var user = RequireAuthenticated();
        if (!_authorization.IsAllowed(user, RolePermission.ControlDocumentAccess)) throw new UnauthorizedAccessException("Only Approvers with document access control permission can manage Viewer access.");
        return user;
    }
    private AuthenticatedUser RequireAuthenticated() => _session.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated user is required.");
    private void ValidateExpiration(DateTime? expiration)
    {
        if (!expiration.HasValue) return;
        if (!_configuration.AllowExpiration) throw new InvalidOperationException("Time-limited Viewer access is disabled.");
        if (expiration.Value.Kind != DateTimeKind.Utc || expiration.Value <= DateTime.UtcNow) throw new ArgumentException("The access expiration must be a future UTC timestamp.", nameof(expiration));
    }
}
