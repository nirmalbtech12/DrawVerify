using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateEditorServiceTests
{
    [Fact(DisplayName = "TSD-05 WorkflowService captures and replays immutable published template mappings")]
    public async Task Published_template_submission_reloads_original_mapping_after_source_change()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-template-workflow-{Guid.NewGuid():N}.db");
        var packageId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var placeholderId = Guid.NewGuid();
        try
        {
            var configuration = new WorkflowConfiguration { PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation }, DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved } };
            var configurations = new SqliteWorkflowConfigurationRepository(path);
            await configurations.SaveAsync(configuration, new WorkflowConfigurationAudit("admin", DateTime.UtcNow, null, 1, "{}", "{}"), null);
            var workflow = new SqliteWorkflowRepository(path, configuration: configuration, configurationRepository: configurations);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var seed = connection.CreateCommand();
                seed.CommandText = "INSERT INTO WorkflowState(PackageId,Status,Revision,UpdatedUtc) VALUES($id,$status,1,$utc)";
                seed.Parameters.AddWithValue("$id", packageId.ToString()); seed.Parameters.AddWithValue("$status", (int)WorkflowStatus.Draft); seed.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
                await seed.ExecuteNonQueryAsync();
            }
            var templates = new SqliteReportTemplateDocumentRepository(path);
            var originalMapping = new TemplateMappingSnapshot { TemplateId = templateId, TemplateVersion = 1, Assignments = new[] { new SignaturePlaceholderAssignment { PlaceholderId = placeholderId, Role = "Maker", AssignedSignerUserId = "maker-1" } }, CustomFieldMappings = new Dictionary<string, string> { ["Department"] = "Quality" } };
            await templates.SaveAsync(new ReportTemplateDocument { TemplateId = templateId, Version = 1, SourceId = Guid.NewGuid(), Content = [1], TemplateMapping = originalMapping });
            var authentication = new SessionAuthenticationService();
            authentication.SignIn(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-1", DisplayName = "Maker One" });
            var documents = new WorkflowDocumentRepository(packageId);
            var service = new WorkflowService(workflow, authentication, configuration, documents: documents, templateDocuments: templates, configurationRepository: configurations, clock: () => new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc));
            var makerSignature = SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", packageId, 1, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
            var result = await service.SubmitForReviewAsync(new SubmissionRequest(packageId) { TemplateId = templateId, TemplateVersion = 1, StepDocumentVersionId = documents.VersionId, MakerSignature = makerSignature });
            Assert.True(result.Succeeded);

            var changedMapping = originalMapping with { CustomFieldMappings = new Dictionary<string, string> { ["Department"] = "Changed" } };
            await templates.SaveAsync(new ReportTemplateDocument { TemplateId = templateId, Version = 2, SourceId = Guid.NewGuid(), Content = [2], TemplateMapping = changedMapping }, 1);
            var reloaded = (await workflow.GetReviewPackageAsync(packageId, 1))!.Submission;
            Assert.NotNull(reloaded.TemplateMapping);
            Assert.Equal(templateId, reloaded.TemplateMapping!.TemplateId);
            Assert.Equal(1, reloaded.TemplateMapping.TemplateVersion);
            Assert.Equal(placeholderId, Assert.Single(reloaded.TemplateMapping.Assignments).PlaceholderId);
            Assert.Equal("Maker", reloaded.TemplateMapping.Assignments[0].Role);
            Assert.Equal("Quality", reloaded.TemplateMapping.CustomFieldMappings["Department"]);
            Assert.Equal(placeholderId, Assert.Single(reloaded.PlaceholderAssignments).PlaceholderId);

            var duplicate = await service.SubmitForReviewAsync(new SubmissionRequest(packageId) { TemplateId = templateId, TemplateVersion = 1, MakerSignature = makerSignature });
            Assert.False(duplicate.Succeeded);
            Assert.Single(await workflow.GetSubmissionsAsync(packageId));
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } }
    }

    [Fact(DisplayName = "TSD-05 mapping snapshot serializes template version and custom mappings")]
    public void Mapping_snapshot_round_trips_as_captured_metadata()
    {
        var snapshot = new TemplateMappingSnapshot
        {
            TemplateId = Guid.NewGuid(),
            TemplateVersion = 4,
            Assignments = new[] { new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Approver", AssignedSignerUserId = "approver-1" } },
            CustomFieldMappings = new Dictionary<string, string> { ["SignerName"] = "approver-1" }
        };

        var restored = System.Text.Json.JsonSerializer.Deserialize<TemplateMappingSnapshot>(System.Text.Json.JsonSerializer.Serialize(snapshot));
        Assert.NotNull(restored);
        Assert.Equal(snapshot.TemplateId, restored!.TemplateId);
        Assert.Equal(snapshot.TemplateVersion, restored.TemplateVersion);
        Assert.Equal(snapshot.Assignments, restored.Assignments);
        Assert.Equal("approver-1", restored.CustomFieldMappings["SignerName"]);
    }
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

    [Fact(DisplayName = "TSD-02 and TSD-03 supported placeholder metadata survives editor insertion")]
    public async Task Supported_roles_and_dynamic_types_are_retained()
    {
        var id = Guid.NewGuid(); var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("admin", [SystemRoles.Admin]));
        var service = CreateService(new FakePublisher(), new FakeDocuments(id, [1]), new FakeEditor(), session);
        var document = new TemplateEditorDocument { DocxBytes = [1] };
        foreach (var role in new[] { "Maker", "Checker", "Approver", "Custom Role" })
            document = await service.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = role, Label = $"{role} Signature", IsRequired = role != "Custom Role" });
        foreach (var type in new[] { TemplatePlaceholderType.Date, TemplatePlaceholderType.DocumentNumber, TemplatePlaceholderType.Revision, TemplatePlaceholderType.ProjectName, TemplatePlaceholderType.CustomText })
            document = await service.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = type, Label = type == TemplatePlaceholderType.CustomText ? "Custom text" : type.ToString() });

        Assert.Equal(9, document.Placeholders.Count);
        Assert.Equal(new[] { "Maker", "Checker", "Approver", "Custom Role" }, document.Placeholders.Take(4).Select(x => x.Role));
        Assert.Equal(new[] { TemplatePlaceholderType.Date, TemplatePlaceholderType.DocumentNumber, TemplatePlaceholderType.Revision, TemplatePlaceholderType.ProjectName, TemplatePlaceholderType.CustomText }, document.Placeholders.Skip(4).Select(x => x.Type));
    }

    private static ReportTemplateEditorService CreateService(FakePublisher repository, FakeDocuments documents, FakeEditor editor, SessionAuthenticationService session) => new(repository, documents, editor, new FakePreview(), session, new RoleAuthorization());

    private sealed class FakeEditor : IWordTemplateEditor
    {
        public Task<TemplateEditorDocument> OpenAsync(byte[] bytes, CancellationToken cancellationToken = default) => Task.FromResult(new TemplateEditorDocument { DocxBytes = bytes, Placeholders = [] });
        public Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default) => Task.FromResult(document with { Placeholders = document.Placeholders.Append(placeholder).ToArray() });
        public Task<byte[]> SaveAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default) => Task.FromResult(document.DocxBytes);
    }

    private sealed class WorkflowDocumentRepository(Guid packageId) : IDocumentRepository
    {
        private readonly Document _document = new()
        {
            DocumentId = packageId,
            DocumentType = DrawVerify.Core.Enums.DocumentType.PdfDrawing,
            OriginalFileName = "package.pdf",
            FileSizeBytes = 1024,
            UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded,
            GeometryStatus = DrawVerify.Core.Enums.GeometryStatus.Processed,
            Version = new DocumentVersion { DocumentId = packageId, VersionId = Guid.NewGuid() }
        };

        public Guid VersionId => _document.Version!.VersionId;

        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Document?>(documentId == _document.DocumentId ? _document : null);
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
