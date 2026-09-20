using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class InternalSharedLinkService : IInternalSharedLinkService
{
    public const string ApprovedOnlyMessage = "Shared links can only be generated for approved documents.";
    public const string InvalidLinkMessage = "Invalid or revoked link.";
    public const string ExpiredLinkMessage = "This link has expired.";
    public const string AuthenticationRequiredMessage = "Authentication is required to access this shared link.";
    public const string UnauthorizedMessage = "You are not authorized to access this shared link.";
    private readonly IDocumentRepository _documents;
    private readonly IInternalSharedLinkRepository _links;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly IDocumentAuditRepository? _audit;
    private readonly ICentralizedLogger? _logger;
    private readonly ISharedLinkIdentityProvider _identity;
    private readonly InternalSharedLinkPolicy _policy;
    private readonly string _baseUrl;

    public InternalSharedLinkService(IDocumentRepository documents, IInternalSharedLinkRepository links, ISessionAuthenticationService session, RoleAuthorization? authorization = null, IDocumentAuditRepository? audit = null, ICentralizedLogger? logger = null, ISharedLinkIdentityProvider? identity = null, InternalSharedLinkPolicy? policy = null, string? baseUrl = null)
    {
        _documents = documents; _links = links; _session = session; _authorization = authorization ?? new(); _audit = audit; _logger = logger; _policy = policy ?? new(); _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://drawverify.internal/share" : baseUrl.TrimEnd('/');
        _identity = identity ?? new SessionSharedLinkIdentityProvider(session, _policy);
    }

    public async Task<InternalSharedLinkDetails> GenerateAsync(Guid documentId, Guid versionId, InternalSharedLinkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        EnsureApprover(); configuration.Validate(); ValidateConfiguration(configuration);
        var document = await _documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The document could not be found.");
        var version = await _documents.GetVersionAsync(documentId, versionId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The document version could not be found.");
        if (document.Status != DocumentStatus.Active) throw new InvalidOperationException(ApprovedOnlyMessage);
        if (!configuration.AllowMultipleLinks && (await _links.ListAsync(documentId, versionId, cancellationToken).ConfigureAwait(false)).Any(x => x.Status(DateTime.UtcNow) is SharedLinkStatus.Active or SharedLinkStatus.Inactive)) throw new InvalidOperationException("A shared link already exists for this document version.");
        var link = CreateLink(documentId, versionId, configuration); var persisted = await _links.CreateAsync(link, cancellationToken).ConfigureAwait(false); await WriteAuditAsync(documentId, versionId, DocumentAuditAction.SharedLinkCreated, new { linkId = link.LinkId, accessLevel = link.AccessLevel.ToString(), expiresUtc = link.ExpiresUtc }, cancellationToken).ConfigureAwait(false); return await DetailsAsync(persisted, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InternalSharedLinkDetails> GetDetailsAsync(Guid linkId, CancellationToken cancellationToken = default)
    { EnsureApprover(); var link = await _links.GetByIdAsync(linkId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The shared link could not be found."); return await DetailsAsync(link, cancellationToken).ConfigureAwait(false); }

    public async Task<IReadOnlyList<InternalSharedLinkDetails>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default)
    { EnsureApprover(); var links = await _links.ListAsync(documentId, versionId, cancellationToken).ConfigureAwait(false); var result = new List<InternalSharedLinkDetails>(); foreach (var link in links) result.Add(await DetailsAsync(link, cancellationToken).ConfigureAwait(false)); return result; }

    public async Task RevokeAsync(Guid linkId, CancellationToken cancellationToken = default)
    { EnsureApprover(); var link = await _links.GetByIdAsync(linkId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The shared link could not be found."); await _links.RevokeAsync(linkId, DateTime.UtcNow, cancellationToken).ConfigureAwait(false); await WriteAuditAsync(link.DocumentId, link.VersionId, DocumentAuditAction.SharedLinkRevoked, new { linkId }, cancellationToken).ConfigureAwait(false); }

    public async Task<InternalSharedLinkDetails> RegenerateAsync(Guid linkId, InternalSharedLinkConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        EnsureApprover(); var old = await _links.GetByIdAsync(linkId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The shared link could not be found."); var settings = configuration ?? new InternalSharedLinkConfiguration { AccessLevel = old.AccessLevel, IsActive = old.IsActive, ExpiresUtc = old.ExpiresUtc, TrackingEnabled = old.TrackingEnabled }; settings.Validate(); ValidateConfiguration(settings, allowMultipleLinkRequest: false); var replacement = CreateLink(old.DocumentId, old.VersionId, settings); var result = await _links.RegenerateAsync(linkId, replacement, DateTime.UtcNow, cancellationToken).ConfigureAwait(false); await WriteAuditAsync(old.DocumentId, old.VersionId, DocumentAuditAction.SharedLinkRegenerated, new { oldLinkId = linkId, newLinkId = result.LinkId }, cancellationToken).ConfigureAwait(false); return await DetailsAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InternalSharedLinkAccessResult> AuthorizeAsync(InternalSharedLinkAccessRequest request, CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuthenticationRequired, AuthenticationRequiredMessage);
        if (string.IsNullOrWhiteSpace(request.Token)) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.InvalidLink, InvalidLinkMessage);
        try
        {
            var link = await _links.FindByTokenAsync(Hash(request.Token), cancellationToken).ConfigureAwait(false); if (link is null) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.InvalidLink, InvalidLinkMessage);
            var status = link.Status(DateTime.UtcNow); if (status == SharedLinkStatus.Expired) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Expired, ExpiredLinkMessage); if (status != SharedLinkStatus.Active) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Inactive, InvalidLinkMessage);
            if (request.ExpectedDocumentId.HasValue && request.ExpectedDocumentId != link.DocumentId || request.ExpectedVersionId.HasValue && request.ExpectedVersionId != link.VersionId) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.VersionMismatch, InvalidLinkMessage);
            if (!_identity.IsInternal(_session.CurrentUser) || !_identity.IsAllowed(_session.CurrentUser, link)) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Unauthorized, UnauthorizedMessage);
            if (request.Download && link.AccessLevel != SharedLinkAccessLevel.ViewAndDownload) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.Unauthorized, "Downloading is not permitted for this shared link.");
            var document = await _documents.GetAsync(link.DocumentId, cancellationToken).ConfigureAwait(false); var version = document is null ? null : await _documents.GetVersionAsync(link.DocumentId, link.VersionId, cancellationToken).ConfigureAwait(false); if (document is null || version is null || document.Status != DocumentStatus.Active) return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.DocumentNotApproved, ApprovedOnlyMessage);
            if (link.TrackingEnabled) await _links.RecordAccessAsync(link.LinkId, _session.CurrentUser.UserId, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            await WriteAuditAsync(link.DocumentId, link.VersionId, DocumentAuditAction.AccessedViaSharedLink, new { linkId = link.LinkId, userId = _session.CurrentUser.UserId, action = "AccessedViaSharedLink" }, cancellationToken).ConfigureAwait(false);
            return new InternalSharedLinkAccessResult { Succeeded = true, Link = link, Document = document };
        }
        catch (Exception exception) { _logger?.Log(exception, "Shared-link authorization", $"Unable to authorize a shared link for user '{_session.CurrentUser.UserId}'."); return InternalSharedLinkAccessResult.Fail(InternalSharedLinkAccessFailure.AuditFailed, "The shared-link access could not be completed."); }
    }

    private async Task<InternalSharedLinkDetails> DetailsAsync(InternalSharedLink link, CancellationToken token) { InternalSharedLinkUsage? usage = null; var unavailable = false; try { if (link.TrackingEnabled) usage = await _links.GetUsageAsync(link.LinkId, _policy.AllowTracking && _policy.AllowedGroups.Count == 0, token).ConfigureAwait(false); } catch (Exception exception) { _logger?.Log(exception, "Shared-link usage", $"Unable to load usage for link {link.LinkId}."); unavailable = true; } return new InternalSharedLinkDetails { Link = link, Status = link.Status(DateTime.UtcNow), AccessCount = usage?.AccessCount, LastAccessUtc = usage?.LastAccessUtc, AccessingUsers = usage?.AccessingUsers, UsageUnavailable = unavailable }; }
    private InternalSharedLink CreateLink(Guid documentId, Guid versionId, InternalSharedLinkConfiguration c) { var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'); return new InternalSharedLink { DocumentId = documentId, VersionId = versionId, Token = Hash(token), Url = $"{_baseUrl}?token={Uri.EscapeDataString(token)}", AccessLevel = c.AccessLevel, IsActive = c.IsActive, ExpiresUtc = c.ExpiresUtc, TrackingEnabled = c.TrackingEnabled, CreatedUtc = DateTime.UtcNow }; }
    private async Task WriteAuditAsync(Guid documentId, Guid versionId, DocumentAuditAction action, object details, CancellationToken token) { if (_audit is null) return; var entry = new AuditEntry { DocumentId = documentId, DocumentVersion = versionId.ToString("D"), UserId = _session.CurrentUser?.UserId ?? "system", ActorRole = _session.CurrentUser?.Roles.FirstOrDefault(), Action = action, TimestampUtc = DateTime.UtcNow, DetailsJson = JsonSerializer.Serialize(details) }; try { await _audit.AppendAsync(entry, token).ConfigureAwait(false); } catch (Exception exception) { _logger?.Log(exception, "Shared-link audit", $"Unable to persist action '{action}' for link lifecycle operation."); throw; } }
    private void EnsureApprover() { var user = _session.CurrentUser; if (user is null || !_authorization.IsAllowed(user, RolePermission.FinalApproval) || !user.HasRole(SystemRoles.Approver)) throw new UnauthorizedAccessException("Only an authenticated Approver can manage shared links."); }
    private void ValidateConfiguration(InternalSharedLinkConfiguration c, bool allowMultipleLinkRequest = true) { if (! _policy.IsAccessLevelAllowed(c.AccessLevel)) throw new InvalidOperationException("The requested access level is not allowed by policy."); if (c.ExpiresUtc.HasValue && !_policy.AllowExpiration) throw new InvalidOperationException("Link expiration is not enabled by policy."); if (c.TrackingEnabled && !_policy.AllowTracking) throw new InvalidOperationException("Link tracking is not enabled by policy."); if (allowMultipleLinkRequest && c.AllowMultipleLinks && !_policy.AllowMultipleLinks) throw new InvalidOperationException("Multiple shared links are not enabled by policy."); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed class SessionSharedLinkIdentityProvider(ISessionAuthenticationService session, InternalSharedLinkPolicy policy) : ISharedLinkIdentityProvider { public bool IsAuthenticated => session.IsAuthenticated; public bool IsInternal(AuthenticatedUser user) => string.IsNullOrWhiteSpace(policy.InternalDomain) || user.Username.EndsWith("@" + policy.InternalDomain, StringComparison.OrdinalIgnoreCase) || user.Roles.Any(x => policy.AllowedRoles.Contains(x)); public bool IsAllowed(AuthenticatedUser user, InternalSharedLink link) => user.Roles.Any(policy.AllowedRoles.Contains); }
}
