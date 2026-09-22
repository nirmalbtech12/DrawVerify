using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateEditorServiceTests
{
    [Fact(DisplayName = "TSD-01, TSD-02, and TSD-03 editor operations preserve document state")]
    public async Task Open_insert_and_save_use_editor_contract()
    {
        var id = Guid.NewGuid();
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("admin", [SystemRoles.Admin]));
        var repository = new FakePublisher();
        var documents = new FakeDocuments(id, [1, 2, 3]);
        var editor = new FakeEditor();
        var service = CreateService(repository, documents, editor, session);
        var document = await service.OpenAsync(id);
        document = await service.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Checker", Label = "Checker Signature" });
        var saved = await service.SaveAsync(new ReportTemplate { Id = id }, document, 1, "admin");
        Assert.Single(document.Placeholders);
        Assert.Equal(2, saved.Version);
        Assert.NotNull(repository.PublishedDocument);
    }

    [Fact(DisplayName = "TSD-02 and TSD-03 invalid placeholders are rejected before persistence")]
    public async Task Invalid_placeholder_does_not_publish()
    {
        var id = Guid.NewGuid(); var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("admin", [SystemRoles.Admin]));
        var repository = new FakePublisher(); var service = CreateService(repository, new FakeDocuments(id, [1]), new FakeEditor(), session);
        var document = new TemplateEditorDocument { DocxBytes = [1], Placeholders = [new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = null, Label = "Missing role" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new ReportTemplate { Id = id }, document, 1, "admin"));
        Assert.Null(repository.PublishedDocument);
    }

    private static ReportTemplateEditorService CreateService(FakePublisher repository, FakeDocuments documents, FakeEditor editor, SessionAuthenticationService session) => new(repository, documents, editor, new FakePreview(), session, new RoleAuthorization());

    private sealed class FakeEditor : IWordTemplateEditor
    {
        public Task<TemplateEditorDocument> OpenAsync(byte[] bytes, CancellationToken cancellationToken = default) => Task.FromResult(new TemplateEditorDocument { DocxBytes = bytes, Placeholders = [] });
        public Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default) => Task.FromResult(document with { Placeholders = document.Placeholders.Append(placeholder).ToArray() });
        public Task<byte[]> SaveAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default) => Task.FromResult(document.DocxBytes);
    }
    private sealed class FakePreview : IWordTemplatePreviewService { public Task<TemplatePreviewData> PreviewAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default) => Task.FromResult(new TemplatePreviewData { DocxBytes = document.DocxBytes }); }
    private sealed class FakeDocuments(Guid id, byte[] bytes) : IReportTemplateDocumentRepository
    {
        public Task<ReportTemplateDocument?> GetAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default) => Task.FromResult<ReportTemplateDocument?>(new ReportTemplateDocument { TemplateId = id, Version = version ?? 1, SourceId = Guid.NewGuid(), Content = bytes });
        public Task<ReportTemplateDocument> SaveAsync(ReportTemplateDocument document, int? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult(document);
        public Task<ReportTemplateSource?> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default) => Task.FromResult<ReportTemplateSource?>(null);
        public Task SaveSourceAsync(ReportTemplateSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordConversionAuditAsync(TemplateConversionAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TemplateConversionAuditEntry>> GetConversionAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemplateConversionAuditEntry>>([]);
    }
    private sealed class FakePublisher : IReportTemplateRepository, IReportTemplatePublisher
    {
        public ReportTemplateDocument? PublishedDocument { get; private set; }
        public Task<ReportTemplate> PublishAsync(ReportTemplate template, int expectedVersion, ReportTemplateAuditEntry audit, ReportTemplateDocument document, CancellationToken cancellationToken = default) { PublishedDocument = document; return Task.FromResult(template with { Version = expectedVersion + 1 }); }
        public Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplate>>([]);
        public Task<ReportTemplate?> GetAsync(Guid id, int? version = null, CancellationToken cancellationToken = default) => Task.FromResult<ReportTemplate?>(null);
        public Task<ReportTemplate> SaveAsync(ReportTemplate template, int expectedVersion, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task DeleteAsync(Guid id, int expectedVersion, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetDefaultAsync(Guid id, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ReportTemplateVersion>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplateVersion>>([]);
        public Task AssignAsync(ReportTemplateAssignment assignment, ReportTemplateAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReportTemplate?> ResolveAsync(string? projectId, string? workflowId, CancellationToken cancellationToken = default) => Task.FromResult<ReportTemplate?>(null);
        public Task<IReadOnlyList<ReportTemplateAuditEntry>> GetAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplateAuditEntry>>([]);
    }
}
