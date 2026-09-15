using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteDocumentAuditRepositoryTests
{
    [Fact]
    public async Task TD_INF_001_round_trips_complete_history_and_isolates_documents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteAuditRepository(path);
            var document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            await repository.AppendAsync(Entry(document, DocumentAuditAction.DocumentCreated, "REV-001", new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.FromHours(5.5))));
            await repository.AppendAsync(Entry(document, DocumentAuditAction.FileUploaded, "REV-001", new DateTimeOffset(2026, 1, 10, 8, 5, 0, TimeSpan.FromHours(5.5)), "{\"file\":\"part.step\"}"));
            await repository.AppendAsync(Entry(other, DocumentAuditAction.DocumentViewed, "REV-002", new DateTimeOffset(2026, 1, 10, 8, 10, 0, TimeSpan.Zero)));
            var records = await repository.QueryAsync(new AuditLogQuery(DocumentId: document));
            Assert.Equal(2, records.Count);
            Assert.Equal(new[] { DocumentAuditAction.DocumentCreated, DocumentAuditAction.FileUploaded }, records.Select(x => x.Action));
            Assert.Equal("maker-001", records[0].ActorId);
            Assert.Equal(TimeSpan.FromHours(5.5), records[0].Timestamp.Offset);
            Assert.Equal("REV-001", records[0].DocumentRevision);
            Assert.Contains("part.step", records[1].EventMetadataJson);
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.AppendAsync(new AuditEntry { AuditId = records[0].AuditId, DocumentId = document, UserId = "maker-001", Action = DocumentAuditAction.DocumentViewed, TimestampOffset = DateTimeOffset.UtcNow, TimestampUtc = DateTime.UtcNow }));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static AuditEntry Entry(Guid document, DocumentAuditAction action, string revision, DateTimeOffset timestamp, string metadata = "{}") => new() { AuditId = Guid.NewGuid(), DocumentId = document, UserId = "maker-001", ActorRole = "Maker", DocumentRevision = revision, DocumentVersion = "1", TimestampOffset = timestamp, TimestampUtc = timestamp.UtcDateTime, Action = action, DetailsJson = "{\"event\":\"audit\"}", EventMetadataJson = metadata };
}
