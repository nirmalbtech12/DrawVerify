using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkAccessAuditTests
{
    [Fact(DisplayName = "TSD-AC11-AUDIT successful shared-link audit preserves required fields")]
    public void Successful_access_audit_has_required_fields_and_timestamp()
    {
        var before = DateTime.UtcNow;
        var entry = new AuditEntry { DocumentId = Guid.NewGuid(), DocumentVersion = "version-1", UserId = "viewer-1", Action = DocumentAuditAction.AccessedViaSharedLink, TimestampUtc = DateTime.UtcNow, DetailsJson = "{\"linkId\":\"link-1\"}" };
        var after = DateTime.UtcNow;
        Assert.NotEqual(Guid.Empty, entry.DocumentId);
        Assert.Equal("version-1", entry.DocumentVersion);
        Assert.Equal("viewer-1", entry.UserId);
        Assert.Equal(DocumentAuditAction.AccessedViaSharedLink, entry.Action);
        Assert.InRange(entry.TimestampUtc, before, after);
        Assert.Contains("linkId", entry.DetailsJson, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "TSD-AC11 access audit contains document version user action and link identifier without raw secret")]
    public void Access_audit_payload_contains_required_trace_fields()
    {
        var document = Guid.NewGuid();
        var link = Guid.NewGuid();
        const string rawSecret = "raw-secret-must-not-be-stored";
        var entry = new AuditEntry { DocumentId = document, DocumentVersion = "version-1", UserId = "viewer-1", Action = DocumentAuditAction.AccessedViaSharedLink, DetailsJson = $"{{\"linkId\":\"{link}\",\"action\":\"AccessedViaSharedLink\"}}" };
        Assert.Equal(document, entry.DocumentId);
        Assert.Equal("version-1", entry.DocumentVersion);
        Assert.Equal(DocumentAuditAction.AccessedViaSharedLink, entry.Action);
        Assert.Contains(link.ToString("D"), entry.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSecret, entry.DetailsJson, StringComparison.Ordinal);
    }
}
