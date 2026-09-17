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

    [Fact(DisplayName = "TSD-AC03-ACTOR-IDENTITY preserves displayed identity, role, and stable ID")]
    public void TSD_AC03_ACTOR_IDENTITY_preserves_display_identity_and_role()
    {
        var entry = new AuditTrailEntry { UserId = "approver-001", UserName = "Approver One", UserRole = "Approver", UserEmail = "approver@example.test" };
        Assert.Equal("Approver One", entry.UserName);
        Assert.Equal("Approver", entry.UserRole);
        Assert.Equal("approver-001", entry.UserId);
        Assert.Equal("approver@example.test", entry.UserEmail);
    }

    [Fact(DisplayName = "TSD-AC05-VERIFICATION-ACTIONS preserves run IDs and verification counts")]
    public void TSD_AC05_VERIFICATION_ACTIONS_preserves_structured_details_and_run_identifier()
    {
        var entry = new AuditTrailEntry { Action = DocumentAuditAction.DimensionsMatchedPartial, Outcome = "Success", Identifier = "run-42", Details = "{\"matched\":8,\"mismatched\":2,\"missing\":1,\"gdtChecked\":4}" };
        Assert.Equal(DocumentAuditAction.DimensionsMatchedPartial, entry.Action);
        Assert.Equal("run-42", entry.Identifier);
        Assert.Contains("missing", entry.Details);
        Assert.Contains("gdtChecked", entry.Details);
    }

    [Theory(DisplayName = "TSD-AC07-ACTION-DETAILS preserves action outcomes and identifiers")]
    [InlineData("Success")]
    [InlineData("Failed")]
    [InlineData("Returned")]
    public void TSD_AC07_ACTION_DETAILS_preserves_representative_outcomes(string outcome)
    {
        var entry = new AuditTrailEntry { Action = DocumentAuditAction.ReturnedForRework, Outcome = outcome, Identifier = "REV-7", IntegrityReference = "hash-7" };
        Assert.Equal(outcome, entry.Outcome);
        Assert.Equal("REV-7", entry.Identifier);
        Assert.Equal("hash-7", entry.IntegrityReference);
    }
}
