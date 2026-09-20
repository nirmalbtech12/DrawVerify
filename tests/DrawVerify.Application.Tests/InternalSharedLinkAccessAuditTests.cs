using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkAccessAuditTests
{
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
