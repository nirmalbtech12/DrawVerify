using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class AuditContractTests
{
    private static readonly Guid Document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void TD_CORE_001_valid_entry_preserves_identity_revision_metadata_and_offset()
    {
        var timestamp = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.FromHours(5.5));
        var entry = new AuditEntry { DocumentId = Document, UserId = "maker-001", ActorRole = "Maker", DocumentRevision = "REV-001", DocumentVersion = "1", TimestampOffset = timestamp, TimestampUtc = timestamp.UtcDateTime, Action = DocumentAuditAction.DocumentCreated, EventMetadataJson = "{\"source\":\"upload\"}", DetailsJson = "{\"name\":\"drawing.step\"}" };
        entry.Validate();

        Assert.Equal(Document, entry.DocumentId);
        Assert.Equal("maker-001", entry.UserId);
        Assert.Equal("REV-001", entry.DocumentRevision);
        Assert.Equal(timestamp, entry.TimestampOffset);
        Assert.Equal("{\"source\":\"upload\"}", entry.EventMetadataJson);
    }

    [Fact]
    public void TD_CORE_001_required_fields_and_query_ranges_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new AuditEntry { UserId = "u", Action = DocumentAuditAction.DocumentCreated, TimestampUtc = DateTime.UtcNow }.Validate());
        Assert.Throws<ArgumentException>(() => new AuditEntry { DocumentId = Document, UserId = "", Action = DocumentAuditAction.DocumentCreated, TimestampUtc = DateTime.UtcNow }.Validate());
        Assert.Throws<ArgumentException>(() => new AuditEntry { DocumentId = Document, UserId = "u", Action = (DocumentAuditAction)999, TimestampUtc = DateTime.UtcNow }.Validate());
        Assert.Throws<ArgumentException>(() => new AuditLogQuery(DateTimeOffset.Parse("2026-01-11T00:00:00+00:00"), DateTimeOffset.Parse("2026-01-10T00:00:00+00:00")).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuditLogQuery(Page: 0).Validate());
        Assert.Throws<ArgumentException>(() => new AuditLogQuery(DocumentId: Guid.Empty).Validate());
    }

    [Fact]
    public void TD_CORE_001_actions_keep_existing_values_and_legacy_json_is_readable()
    {
        Assert.Equal(1, (int)DocumentAuditAction.Uploaded);
        Assert.Equal(37, (int)DocumentAuditAction.Approved);
        Assert.Equal(49, (int)DocumentAuditAction.DocumentCreated);
        var json = JsonSerializer.Serialize(new AuditEntry { DocumentId = Document, UserId = "legacy", Action = DocumentAuditAction.Uploaded, DetailsJson = "{\"legacy\":true}" });
        var restored = JsonSerializer.Deserialize<AuditEntry>(json)!;
        Assert.Equal("{\"legacy\":true}", restored.DetailsJson);
        Assert.Equal(DocumentAuditAction.Uploaded, restored.Action);
    }
}
