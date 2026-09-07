using System.Text;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class AuditLogService
{
    private readonly IAuthenticationAuditRepository _repository;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;
    private AuditLogQuery? _lastQuery;
    private AuditLogPage? _lastPage;
    public AuditLogService(IAuthenticationAuditRepository repository, ISessionAuthenticationService session, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null) => (_repository, _session, _authorization, _logger) = (repository, session, authorization ?? new RoleAuthorization(), logger);
    public async Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        try { var page = await _repository.QueryAsync(query, cancellationToken); _lastQuery = query; _lastPage = page; return page; }
        catch (Exception ex) { _logger?.Log(ex, "Audit log query", "Unable to retrieve audit entries."); throw; }
    }
    public async Task<string> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        try
        {
            var page = _lastPage is not null && CanReuseForExport(query, _lastQuery) ? _lastPage : await QueryAsync(query, cancellationToken);
            var b = new StringBuilder("Actor,AffectedUsername,TimestampUtc,Action,Result,FailureReason,RolesBefore,RolesAfter\r\n"); foreach (var e in page.Entries) b.AppendJoin(',', new[] { e.ActorUsername, e.Username, e.TimestampUtc.ToString("O"), e.Action, e.Result, e.FailureReason, e.RolesBefore, e.RolesAfter }.Select(Escape)).Append("\r\n"); return b.ToString();
        }
        catch (Exception ex) { _logger?.Log(ex, "Audit log export", "Unable to export audit entries."); throw; }
    }
    private static bool CanReuseForExport(AuditLogQuery requested, AuditLogQuery? previous)
        => previous is not null
           && string.Equals(requested.Action, previous.Action, StringComparison.Ordinal)
           && string.Equals(requested.ActorUsername, previous.ActorUsername, StringComparison.Ordinal)
           && requested.FromUtc == previous.FromUtc
           && requested.ToUtc == previous.ToUtc
           && requested.Page == previous.Page
           && requested.PageSize == previous.PageSize
           && (requested.Username is null || string.Equals(requested.Username, previous.Username, StringComparison.Ordinal));
    private void EnsureAuthorized() { if (!_authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewAuditLog)) throw new UnauthorizedAccessException("Only administrators can view audit logs."); }
    private static string Escape(string? value) { value ??= string.Empty; return value.Contains(',', StringComparison.Ordinal) || value.Contains('"') || value.Contains('\n') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value; }
}
