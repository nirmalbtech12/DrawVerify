using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportServiceTemplateVersionTests
{
    [Fact(DisplayName = "TSD-AC10 TSD-AC15 report generation records resolved template provenance")]
    public async Task Generated_report_contains_active_template_id_and_version()
    {
        var id = Guid.NewGuid();
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }));
        var renderer = new CapturingRenderer();
        var repository = new TemplateRepository(new ReportTemplate { Id = id, Version = 4, IsOrganizationDefault = true });
        var service = new ReportService(new Sessions(), auth, pdfGenerator: renderer, audit: new DocumentAudit(), documents: new Documents(), templates: repository);
        await service.GeneratePdfAsync(Guid.NewGuid());
        Assert.Null(repository.ProjectId);
        Assert.Null(repository.WorkflowId);
        Assert.NotNull(renderer.Template);
        Assert.Equal(id, renderer.Template!.Id);
        Assert.Equal(4, renderer.Template.Version);
        Assert.Equal(id, renderer.Report!.Metadata.TemplateId);
        Assert.Equal(4, renderer.Report.Metadata.TemplateVersion);
        Assert.False(renderer.LegacyOverloadCalled);
    }

    private sealed class CapturingRenderer : IPdfReportGenerator
    {
        public VerificationReport? Report { get; private set; }
        public ReportTemplate? Template { get; private set; }
        public bool LegacyOverloadCalled { get; private set; }
        public Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default)
        {
            LegacyOverloadCalled = true;
            throw new InvalidOperationException("The legacy PDF overload must not be used for an active template.");
        }
        public Task<byte[]> GeneratePdfAsync(VerificationReport report, ReportTemplate template, CancellationToken cancellationToken = default)
        {
            Report = report;
            Template = template;
            return Task.FromResult(new byte[] { 1 });
        }
    }
    private sealed class Sessions : IVerificationSessionRepository { public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = id, StepDocumentId = id, DxfDocumentId = id }); public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DrawVerify.Core.Enums.DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(Array.Empty<DimensionMatch>()); public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotSupportedException(); public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DrawVerify.Core.Enums.DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class DocumentAudit : IDocumentAuditRepository, IAuditLogger { public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentAuditRecord>>(Array.Empty<DocumentAuditRecord>()); public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class Documents : IDocumentRepository { public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(new Document { DocumentId = documentId, OriginalFileName = "sample" }); }
    private sealed class TemplateRepository(ReportTemplate template) : IReportTemplateRepository
    {
        public string? ProjectId { get; private set; }
        public string? WorkflowId { get; private set; }
        public Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplate>>(new[] { template });
        public Task<ReportTemplate?> GetAsync(Guid id, int? version = null, CancellationToken cancellationToken = default) => Task.FromResult<ReportTemplate?>(template.Id == id ? template : null);
        public Task<ReportTemplate> SaveAsync(ReportTemplate t, int expectedVersion, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.FromResult(t);
        public Task DeleteAsync(Guid id, int expectedVersion, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetDefaultAsync(Guid id, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ReportTemplateVersion>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplateVersion>>(Array.Empty<ReportTemplateVersion>());
        public Task AssignAsync(ReportTemplateAssignment assignment, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReportTemplate?> ResolveAsync(string? projectId, string? workflowId, CancellationToken cancellationToken = default) { ProjectId = projectId; WorkflowId = workflowId; return Task.FromResult<ReportTemplate?>(template); }
        public Task<IReadOnlyList<ReportTemplateAuditEntry>> GetAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplateAuditEntry>>(Array.Empty<ReportTemplateAuditEntry>());
    }
}
