namespace DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Builds verification report content; delegates rendering to IReportGenerator.
/// </summary>
public sealed class ReportService
{
    private readonly IVerificationSessionRepository _sessions;
    private readonly ISessionAuthenticationService _authentication;
    private readonly RoleAuthorization _authorization;
    private readonly IReportGenerator _generator;
    public ReportService(IVerificationSessionRepository sessions, ISessionAuthenticationService authentication, RoleAuthorization? authorization = null, IReportGenerator? generator = null)
    { _sessions = sessions; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization(); _generator = generator ?? new MarkdownReportGenerator(); }
    public async Task<string> GenerateMarkdownAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewDimensionMatchResults)) throw new UnauthorizedAccessException("Only authenticated Maker or Checker users may access verification reports.");
        var matches = await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await _generator.GenerateMarkdownAsync(new VerificationReport { DimensionMatches = matches }, cancellationToken).ConfigureAwait(false);
    }
}
