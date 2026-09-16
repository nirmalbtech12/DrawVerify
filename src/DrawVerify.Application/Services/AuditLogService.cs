using System.Text;
using System.Text.Json;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class AuditLogService
{
    private readonly IAuthenticationAuditRepository _repository;
    private readonly IDocumentAuditRepository? _documentRepository;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;
    private AuditLogQuery? _lastQuery;
    private AuditLogPage? _lastPage;
    public AuditLogService(IAuthenticationAuditRepository repository, ISessionAuthenticationService session, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null) => (_repository, _session, _authorization, _logger) = (repository, session, authorization ?? new RoleAuthorization(), logger);
    public AuditLogService(IDocumentAuditRepository repository, ISessionAuthenticationService session, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null) : this(new NullAuthenticationAuditRepository(), session, authorization, logger) => _documentRepository = repository;
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
            var page = _lastPage is not null && query == _lastQuery ? _lastPage : await QueryAsync(query, cancellationToken);
            var b = new StringBuilder("Actor,AffectedUsername,TimestampUtc,Action,Result,FailureReason,RolesBefore,RolesAfter\r\n");
            foreach (var entry in page.Entries)
            {
                var affectedUsername = entry.Username;
                b.AppendJoin(',', new[] { entry.ActorUsername, affectedUsername, entry.TimestampUtc.ToString("O"), entry.Action, entry.Result, entry.FailureReason, entry.RolesBefore, entry.RolesAfter }.Select(Escape)).Append("\r\n");
            }
            return b.ToString();
        }
        catch (Exception ex) { _logger?.Log(ex, "Audit log export", "Unable to export audit entries."); throw; }
    }
    private void EnsureAuthorized() { if (!_authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewAuditLog)) throw new UnauthorizedAccessException("Only administrators can view audit logs."); }
    private static string Escape(string? value) { value ??= string.Empty; return value.Contains(',', StringComparison.Ordinal) || value.Contains('"') || value.Contains('\n') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value; }
    public async Task<IReadOnlyList<DocumentAuditRecord>> QueryDocumentAsync(Guid documentId, AuditLogQuery? query = null, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(); if (_documentRepository is null) throw new NotSupportedException("Document audit querying is not configured.");
        var requested = query ?? new AuditLogQuery(); query = new AuditLogQuery(requested.FromUtc, requested.ToUtc, requested.Action, requested.Username, requested.ActorUsername, requested.Page, requested.PageSize, documentId, requested.WorkflowStatus, requested.Revision, requested.Search); query.Validate();
        try { await _documentRepository.VerifyIntegrityAsync(documentId, cancellationToken); return await _documentRepository.QueryAsync(query, cancellationToken); }
        catch (Exception ex) { _logger?.Log(ex, "Document audit query", $"Unable to retrieve audit entries for document {documentId}."); throw; }
    }
    public async Task<string> ExportDocumentCsvAsync(Guid documentId, AuditLogQuery? query = null, CancellationToken cancellationToken = default)
    {
        var entries = await QueryDocumentAsync(documentId, query, cancellationToken); var b = new StringBuilder("AuditId,DocumentId,Revision,Version,Actor,Role,Timestamp,Action,Details,Metadata,Fingerprint,FingerprintAlgorithm,FingerprintCanonicalizationVersion,PreviousStatus,NewStatus,Predecessor,IntegrityHash\r\n");
        foreach (var e in entries)
        {
            var fingerprint = ReadDetail(e.DetailsJson, "fingerprint");
            var algorithm = ReadDetail(e.DetailsJson, "fingerprintAlgorithm");
            var canonicalization = ReadDetail(e.DetailsJson, "fingerprintCanonicalizationVersion");
            b.AppendJoin(',', new[] { e.AuditId.ToString(), e.DocumentId.ToString(), e.DocumentRevision, e.DocumentVersion, e.ActorId, e.ActorRole, e.Timestamp.ToString("O"), e.Action.ToString(), e.DetailsJson, e.EventMetadataJson, fingerprint, algorithm, canonicalization, e.PreviousWorkflowStatus, e.NewWorkflowStatus, e.PredecessorAuditId?.ToString(), e.IntegrityHash }.Select(Escape)).Append("\r\n");
        }
        return b.ToString();
    }
    private static string? ReadDetail(string json, string property)
    {
        try { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty(property, out var value) ? value.ToString() : null; }
        catch (JsonException) { return null; }
    }
    private sealed class NullAuthenticationAuditRepository : IAuthenticationAuditRepository { public Task WriteAsync(AuthenticationAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask; }
}
