using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class AuditLogViewModelDocumentHistoryTests
{
    [Fact]
    public async Task TD_UI_001_approver_sees_filtered_chronological_document_history_and_export()
    {
        var document = Guid.NewGuid(); var repository = new RepositoryFake(new[] { Record(document, "REV-001", DocumentAuditAction.DocumentCreated), Record(document, "REV-002", DocumentAuditAction.Approved) });
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        var vm = new AuditLogViewModel(new AuditLogService(repository, session)) { DocumentId = document, Revision = "REV-002", Search = "approval" };
        await vm.LoadDocumentAsync(); await vm.ExportDocumentAsync();
        Assert.Single(vm.DocumentEntries); Assert.Equal("REV-002", vm.DocumentEntries[0].DocumentRevision); Assert.Equal("Audit trail exported.", vm.Message); Assert.Contains("AuditId", vm.Csv); Assert.Equal("REV-002", repository.LastQuery!.Revision); Assert.Equal("approval", repository.LastQuery.Search);
    }

    [Fact]
    public async Task TD_UI_001_denied_role_has_visible_message_and_no_data()
    {
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" }));
        var vm = new AuditLogViewModel(new AuditLogService(new RepositoryFake(Array.Empty<DocumentAuditRecord>()), session)) { DocumentId = Guid.NewGuid() };
        await vm.LoadDocumentAsync();
        Assert.Contains("authorized", vm.Message, StringComparison.OrdinalIgnoreCase); Assert.Empty(vm.DocumentEntries);
    }

    private static DocumentAuditRecord Record(Guid id, string revision, DocumentAuditAction action) => new() { AuditId = Guid.NewGuid(), DocumentId = id, DocumentRevision = revision, DocumentVersion = revision, ActorId = "approver-001", ActorRole = "Approver", Timestamp = DateTimeOffset.UtcNow, Action = action, DetailsJson = action == DocumentAuditAction.Approved ? "approval" : "created" };
    private sealed class RepositoryFake(IReadOnlyList<DocumentAuditRecord> records) : IDocumentAuditRepository
    {
        public AuditLogQuery? LastQuery { get; private set; }
        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) { LastQuery = query; var output = string.IsNullOrWhiteSpace(query.Revision) ? records : records.Where(x => x.DocumentRevision == query.Revision).ToArray(); return Task.FromResult<IReadOnlyList<DocumentAuditRecord>>(output); }
        public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
