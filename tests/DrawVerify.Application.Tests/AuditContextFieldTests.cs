using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditContextFieldTests
{
    [Fact]
    public void TD_APP_002_context_fields_capture_comments_decisions_files_metadata_and_access()
    {
        var document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var timestamp = DateTimeOffset.Parse("2026-01-10T08:00:00+05:30");
        var record = new DocumentAuditRecord { DocumentId = document, DocumentRevision = "REV-002", DocumentVersion = "2", ActorId = "approver-001", ActorRole = "Approver", Timestamp = timestamp, Action = DocumentAuditAction.Approved, PreviousWorkflowStatus = "InReview", NewWorkflowStatus = "Approved", DetailsJson = "{\"comment\":\"Looks good\",\"target\":\"DIM-4\",\"fileName\":\"part.step\",\"oldValue\":\"A\",\"newValue\":\"B\",\"accessMethod\":\"web viewer\"}", EventMetadataJson = "{\"target\":\"DIM-4\",\"oldValue\":\"A\",\"newValue\":\"B\"}" };
        Assert.Equal(timestamp.Offset, record.Timestamp.Offset);
        Assert.Equal("InReview", record.PreviousWorkflowStatus);
        Assert.Equal("Approved", record.NewWorkflowStatus);
        Assert.Contains("DIM-4", record.DetailsJson);
        Assert.Contains("oldValue", record.EventMetadataJson);
    }
}
