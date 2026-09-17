using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditFailureObservabilityTests
{
    [Fact(DisplayName = "REV-KAN71-001 missing document audit capability fails visibly and emits no generation event")]
    public async Task REV_KAN71_001_missing_document_audit_capability_does_not_generate_pdf_or_event()
    {
        var document = Guid.NewGuid();
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }) { UserId = "checker" });
        var audit = new RecordingAuditLogger();
        var renderer = new RecordingPdfGenerator();
        var logger = new LoggerFake();
        var service = new ReportService(new ReportSessions(document), authentication, audit: audit, pdfGenerator: renderer, logger: logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GeneratePdfAsync(Guid.NewGuid()));
        Assert.False(renderer.Called);
        Assert.Empty(audit.Entries);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.NotNull(logger.Exception);
        Assert.Equal("Verification PDF report generation", logger.Operation);
        Assert.Contains("verificationSessionId=", logger.Context!, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "TSD-AC09-TAMPER-EVIDENCE exposes integrity failures with document context")]
    public async Task TSD_AC09_TAMPER_EVIDENCE_exposes_integrity_failure_context()
    {
        var logger = new LoggerFake(); var document = Guid.NewGuid(); var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AuditLogService(new FailingRepository(), session, logger: logger).QueryDocumentAsync(document));
        Assert.Contains(document.ToString(), logger.Context); Assert.NotNull(logger.Exception);
    }

    [Fact]
    public async Task TD_APP_005_repository_and_integrity_failures_are_logged_and_rethrown()
    {
        var logger = new LoggerFake(); var document = Guid.NewGuid(); var repository = new FailingRepository();
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        var service = new AuditLogService(repository, session, logger: logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueryDocumentAsync(document));
        Assert.Contains(document.ToString(), logger.Context!); Assert.Contains("Document audit query", logger.Operation!);
    }

    [Fact]
    public async Task TD_APP_005_invalid_range_is_rejected_without_repository_access()
    {
        var repository = new FailingRepository(); var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        await Assert.ThrowsAsync<ArgumentException>(() => new AuditLogService(repository, session).QueryDocumentAsync(Guid.NewGuid(), new AuditLogQuery(DateTimeOffset.MaxValue, DateTimeOffset.MinValue)));
        Assert.Equal(0, repository.Calls);
    }

    [Fact(DisplayName = "TD-010 audit failures retain actual exception and document context")]
    public async Task Checker_audit_failure_keeps_diagnostics_and_does_not_succeed()
    {
        var logger = new LoggerFake();
        var document = Guid.NewGuid();
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("administrator", new[] { "Admin" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AuditLogService(new FailingRepository(), session, logger: logger).QueryDocumentAsync(document));
        Assert.Contains(document.ToString(), logger.Context);
        Assert.NotNull(logger.Exception);
        Assert.Equal("integrity failure", logger.Exception!.Message);
        Assert.Equal("Document audit query", logger.Operation);
    }

    [Fact(DisplayName = "TD-010 Checker diagnostics retain context without raw signature payload")]
    public void Checker_diagnostic_contract_does_not_place_payload_in_context()
    {
        const string rawPayload = "typed-checker-signature-sensitive-content";
        var logger = new LoggerFake();
        logger.Log(new InvalidOperationException(rawPayload), "Checker signature", "Unable to persist Checker signature for document doc-001 revision 2.");
        Assert.Equal(rawPayload, logger.Exception!.Message);
        Assert.DoesNotContain(rawPayload, logger.Context!);
        Assert.Contains("document doc-001 revision 2", logger.Context);
    }

    private sealed class FailingRepository : IDocumentAuditRepository
    {
        public int Calls { get; private set; }
        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) { Calls++; return Task.FromException<IReadOnlyList<DocumentAuditRecord>>(new InvalidOperationException("database failure")); }
        public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromException(new InvalidOperationException("integrity failure"));
    }
    private sealed class LoggerFake : ICentralizedLogger
    {
        public Exception? Exception { get; private set; }
        public string? Operation { get; private set; }
        public string? Context { get; private set; }
        public void Log(Exception exception, string operation, string context) => (Exception, Operation, Context) = (exception, operation, context);
    }

    private sealed class ReportSessions(Guid documentId) : IVerificationSessionRepository
    {
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id, StepDocumentId = documentId, DxfDocumentId = documentId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(Array.Empty<DimensionMatch>());
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private sealed class RecordingPdfGenerator : IPdfReportGenerator
    {
        public bool Called { get; private set; }
        public Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default) { Called = true; return Task.FromResult(new byte[] { 37, 80, 68, 70 }); }
    }
}
