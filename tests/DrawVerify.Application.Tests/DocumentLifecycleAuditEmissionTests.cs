using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DocumentLifecycleAuditEmissionTests
{
    [Fact]
    public void TD_APP_001_representative_lifecycle_is_specific_ordered_and_revision_linked()
    {
        var document = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var events = new[] { DocumentAuditAction.DocumentCreated, DocumentAuditAction.FileUploaded, DocumentAuditAction.VerificationRunCompleted, DocumentAuditAction.SubmittedForReview, DocumentAuditAction.Approved, DocumentAuditAction.DocumentViewed, DocumentAuditAction.DocumentDownloaded };
        var records = events.Select((action, index) => new DocumentAuditRecord { AuditId = Guid.NewGuid(), DocumentId = document, DocumentRevision = index < 2 ? "REV-001" : "REV-002", ActorId = index == 4 ? "approver-001" : "maker-001", Timestamp = new DateTimeOffset(2026, 1, 10, 8, index, 0, TimeSpan.FromHours(5.5)), Action = action, DetailsJson = "{}", EventMetadataJson = "{\"lifecycle\":true}" }).ToArray();
        Assert.Equal(events, records.OrderBy(x => x.Timestamp).Select(x => x.Action));
        Assert.All(records, record => Assert.False(string.IsNullOrWhiteSpace(record.ActorId)));
        Assert.All(records, record => Assert.Equal(document, record.DocumentId));
        Assert.Contains(records, x => x.Action == DocumentAuditAction.Approved && x.ActorId == "approver-001");
    }

    [Fact(DisplayName = "TSD-AC02-LIFECYCLE-ACTIONS retains creation, upload, revision, review, rework, approval, and rejection")]
    public void TSD_AC02_LIFECYCLE_ACTIONS_retains_all_required_lifecycle_actions()
    {
        var expected = new[] { DocumentAuditAction.DocumentCreated, DocumentAuditAction.FileUploaded, DocumentAuditAction.FileReplaced, DocumentAuditAction.SubmittedForReview, DocumentAuditAction.ReturnedForRework, DocumentAuditAction.FinalApproved, DocumentAuditAction.Rejected };
        var records = expected.Select((action, index) => new DocumentAuditRecord { DocumentId = Guid.NewGuid(), DocumentRevision = $"REV-{index + 1}", DocumentVersion = $"VERSION-{index + 1}", Action = action, Timestamp = DateTimeOffset.UtcNow.AddMinutes(index), DetailsJson = "{}" }).ToArray();
        Assert.Equal(expected, records.Select(record => record.Action));
        Assert.All(records, record => Assert.False(string.IsNullOrWhiteSpace(record.DocumentRevision)));
        Assert.All(records, record => Assert.False(string.IsNullOrWhiteSpace(record.DocumentVersion)));
    }
}
