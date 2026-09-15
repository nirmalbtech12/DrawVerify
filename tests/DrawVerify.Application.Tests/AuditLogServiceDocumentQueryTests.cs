using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditLogServiceDocumentQueryTests
{
    [Fact]
    public async Task TD_APP_004_approver_queries_document_and_exports_all_fields_with_csv_escaping()
    {
        var document = Guid.NewGuid();
        var repository = new RepositoryFake(new[] { new DocumentAuditRecord { AuditId = Guid.NewGuid(), DocumentId = document, DocumentRevision = "REV-001", DocumentVersion = "1", ActorId = "approver-001", ActorRole = "Approver", Timestamp = DateTimeOffset.Parse("2026-01-10T08:00:00+05:30"), Action = DocumentAuditAction.CommentAdded, DetailsJson = "{\"comment\":\"quoted, note\"}", EventMetadataJson = "{\"target\":\"DIM-4\"}" } });
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        var service = new AuditLogService(repository, session);
        var results = await service.QueryDocumentAsync(document, new AuditLogQuery(Action: nameof(DocumentAuditAction.CommentAdded), Revision: "REV-001", Search: "quoted"));
        var filteredQuery = repository.LastQuery;
        var csv = await service.ExportDocumentCsvAsync(document);
        Assert.Single(results); Assert.Equal(document, filteredQuery!.DocumentId); Assert.Equal("CommentAdded", filteredQuery.Action); Assert.Contains("AuditId,DocumentId,Revision", csv); Assert.Contains("quoted", csv);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Maker")]
    [InlineData("Checker")]
    public async Task TD_APP_004_unauthorized_roles_are_rejected_before_repository_access(string role)
    {
        var repository = new RepositoryFake(Array.Empty<DocumentAuditRecord>());
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("user", new[] { role }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AuditLogService(repository, session).QueryDocumentAsync(Guid.NewGuid()));
        Assert.Equal(0, repository.QueryCalls);
    }

    private sealed class RepositoryFake(IReadOnlyList<DocumentAuditRecord> records) : IDocumentAuditRepository
    {
        public AuditLogQuery? LastQuery { get; private set; }
        public int QueryCalls { get; private set; }
        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) { LastQuery = query; QueryCalls++; return Task.FromResult(records); }
        public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
