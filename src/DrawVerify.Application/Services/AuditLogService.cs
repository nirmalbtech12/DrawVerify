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
        var entries = await QueryDocumentAsync(documentId, query, cancellationToken); var b = new StringBuilder("AuditId,DocumentId,Revision,Version,Actor,Role,Timestamp,Action,Details,Metadata,Fingerprint,RecomputedFingerprint,VerificationResult,FingerprintAlgorithm,FingerprintCanonicalizationVersion,MakerSignature,MakerUserId,MakerDisplayName,MakerRole,MakerTimestampUtc,MakerMethod,MakerTitle,MakerDepartment,CheckerSignature,CheckerUserId,CheckerDisplayName,CheckerRole,CheckerTimestampUtc,CheckerMethod,CheckerTitle,CheckerDepartment,ApproverSignature,ApproverUserId,ApproverDisplayName,ApproverRole,ApproverTimestampUtc,ApproverMethod,StampId,StampApproverUserId,StampApproverDisplayName,StampApproverRole,StampAppliedUtc,StampFingerprintReference,StampIntegrityHash,PreviousStatus,NewStatus,Predecessor,IntegrityHash\r\n");
        foreach (var e in entries)
        {
            var fingerprint = ReadDetail(e.DetailsJson, "storedFingerprint") ?? ReadDetail(e.DetailsJson, "fingerprint");
            var recomputed = ReadDetail(e.DetailsJson, "recomputedFingerprint");
            var verificationResult = ReadDetail(e.DetailsJson, "result") ?? "Unavailable";
            var algorithm = ReadDetail(e.DetailsJson, "fingerprintAlgorithm");
            var canonicalization = ReadDetail(e.DetailsJson, "fingerprintCanonicalizationVersion");
            var makerSignature = ReadDetail(e.DetailsJson, "signatureCanonical");
            var makerUserId = ReadDetail(e.DetailsJson, "makerUserId");
            var makerDisplayName = ReadDetail(e.DetailsJson, "makerDisplayName");
            var makerRole = ReadDetail(e.DetailsJson, "makerRole");
            var makerTimestamp = ReadDetail(e.DetailsJson, "signatureTimestampUtc");
            var makerMethod = ReadDetail(e.DetailsJson, "signatureMethod");
            var makerTitle = ReadDetail(e.DetailsJson, "signatureTitle");
            var makerDepartment = ReadDetail(e.DetailsJson, "signatureDepartment");
            var checkerSignature = ReadDetail(e.DetailsJson, "checkerSignatureCanonical");
            var checkerUserId = ReadDetail(e.DetailsJson, "checkerUserId");
            var checkerDisplayName = ReadDetail(e.DetailsJson, "checkerDisplayName");
            var checkerRole = ReadDetail(e.DetailsJson, "checkerRole") ?? (e.Action == DrawVerify.Core.Enums.DocumentAuditAction.CheckerSignatureApplied ? "Checker" : null);
            var checkerTimestamp = ReadDetail(e.DetailsJson, "checkerSignatureTimestampUtc");
            var checkerMethod = ReadDetail(e.DetailsJson, "checkerSignatureMethod");
            var checkerTitle = ReadDetail(e.DetailsJson, "checkerSignatureTitle");
            var checkerDepartment = ReadDetail(e.DetailsJson, "checkerSignatureDepartment");
            var approverSignature = ReadDetail(e.DetailsJson, "approverSignatureCanonical");
            var approverUserId = ReadDetail(e.DetailsJson, "approverUserId");
            var approverDisplayName = ReadDetail(e.DetailsJson, "approverDisplayName");
            var approverRole = ReadDetail(e.DetailsJson, "approverRole") ?? (e.Action == DrawVerify.Core.Enums.DocumentAuditAction.ApproverSignatureApplied ? "Approver" : null);
            var approverTimestamp = ReadDetail(e.DetailsJson, "approverSignatureTimestampUtc");
            var approverMethod = ReadDetail(e.DetailsJson, "approverSignatureMethod");
            var stampId = ReadDetail(e.DetailsJson, "stampId");
            var stampApproverUserId = ReadDetail(e.DetailsJson, "stampApproverUserId");
            var stampApproverDisplayName = ReadDetail(e.DetailsJson, "stampApproverDisplayName");
            var stampApproverRole = ReadDetail(e.DetailsJson, "stampApproverRole");
            var stampAppliedUtc = ReadDetail(e.DetailsJson, "stampAppliedUtc");
            var stampFingerprintReference = ReadDetail(e.DetailsJson, "fingerprintReference") ?? ReadDetail(e.DetailsJson, "stampFingerprintReference");
            var stampIntegrityHash = ReadDetail(e.DetailsJson, "stampIntegrityHash");
            if (e.Action == DrawVerify.Core.Enums.DocumentAuditAction.ApproverSignatureApplied && (string.IsNullOrWhiteSpace(approverSignature) || string.IsNullOrWhiteSpace(approverUserId) || string.IsNullOrWhiteSpace(approverDisplayName) || string.IsNullOrWhiteSpace(approverRole) || string.IsNullOrWhiteSpace(approverTimestamp) || string.IsNullOrWhiteSpace(approverMethod))) throw new InvalidDataException("Approver signature audit data is incomplete.");
            if (e.Action == DrawVerify.Core.Enums.DocumentAuditAction.MakerSignatureApplied && (string.IsNullOrWhiteSpace(makerSignature) || string.IsNullOrWhiteSpace(makerUserId) || string.IsNullOrWhiteSpace(makerDisplayName) || string.IsNullOrWhiteSpace(makerRole) || string.IsNullOrWhiteSpace(makerTimestamp) || string.IsNullOrWhiteSpace(makerMethod))) throw new InvalidDataException("Maker signature audit data is incomplete.");
            b.AppendJoin(',', new[] { e.AuditId.ToString(), e.DocumentId.ToString(), e.DocumentRevision, e.DocumentVersion, e.ActorId, e.ActorRole, e.Timestamp.ToString("O"), e.Action.ToString(), e.DetailsJson, e.EventMetadataJson, fingerprint, recomputed, verificationResult, algorithm, canonicalization, makerSignature, makerUserId, makerDisplayName, makerRole, makerTimestamp, makerMethod, makerTitle, makerDepartment, checkerSignature, checkerUserId, checkerDisplayName, checkerRole, checkerTimestamp, checkerMethod, checkerTitle, checkerDepartment, approverSignature, approverUserId, approverDisplayName, approverRole, approverTimestamp, approverMethod, stampId, stampApproverUserId, stampApproverDisplayName, stampApproverRole, stampAppliedUtc, stampFingerprintReference, stampIntegrityHash, e.PreviousWorkflowStatus, e.NewWorkflowStatus, e.PredecessorAuditId?.ToString(), e.IntegrityHash }.Select(Escape)).Append("\r\n");
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
