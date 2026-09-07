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
    private AuditLogQuery? _lastQuery;
    private AuditLogPage? _lastPage;
    public AuditLogService(IAuthenticationAuditRepository repository, ISessionAuthenticationService session, RoleAuthorization? authorization = null) => (_repository, _session, _authorization) = (repository, session, authorization ?? new RoleAuthorization());
    public async Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var page = await _repository.QueryAsync(query, cancellationToken);
        _lastQuery = query; _lastPage = page;
        return page;
    }
    public async Task<string> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var page = _lastPage is not null && IsCompatible(query, _lastQuery) ? _lastPage : await QueryAsync(query, cancellationToken);
        var b = new StringBuilder("Actor,AffectedUsername,TimestampUtc,Action,Result,FailureReason,RolesBefore,RolesAfter\r\n"); foreach (var e in page.Entries) b.AppendJoin(',', new[] { e.ActorUsername, e.Username, e.TimestampUtc.ToString("O"), e.Action, e.Result, e.FailureReason, e.RolesBefore, e.RolesAfter }.Select(Escape)).Append("\r\n"); return b.ToString();
    }
    private static bool IsCompatible(AuditLogQuery requested, AuditLogQuery? previous) => previous is not null && string.Equals(requested.Action, previous.Action, StringComparison.Ordinal) && requested.FromUtc == previous.FromUtc && requested.ToUtc == previous.ToUtc && string.Equals(requested.ActorUsername, previous.ActorUsername, StringComparison.Ordinal) && (requested.Username is null || string.Equals(requested.Username, previous.Username, StringComparison.Ordinal));
    private void EnsureAuthorized() { if (!_authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewAuditLog)) throw new UnauthorizedAccessException("Only administrators can view audit logs."); }
    private static string Escape(string? value) { value ??= string.Empty; return value.Contains(',', StringComparison.Ordinal) || value.Contains('"') || value.Contains('\n') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value; }
}
