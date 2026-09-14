using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteFinalApprovalPersistenceTests
{
    [Fact(DisplayName = "T04 migration and T07 snapshot decision are repeatable")]
    public async Task T04_T07_migration_persists_final_decision_and_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"final-approval-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteWorkflowRepository(path); var id = Guid.NewGuid(); var submission = Submission(id);
            await repo.SubmitAtomicallyAsync(new WorkflowState { PackageId = id, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(id, DocumentAuditAction.SubmissionSucceeded, "maker"));
            await repo.SaveReviewDecisionAsync((await repo.GetReviewPackageAsync(id, 1))!, Decision(id, ReviewDecisionType.Approve), WorkflowStatus.FinallyApproved, Audit(id, DocumentAuditAction.FinalApproved));
            var package = await repo.GetReviewPackageAsync(id, 1); Assert.NotNull(package); Assert.True(package!.PriorDecision!.IsFinalApproval); Assert.Equal("snapshot", package.PriorDecision.SnapshotReference);
            await repo.LockRevisionAsync(id, 1); await repo.LockRevisionAsync(id, 1);
            var history = await repo.GetDecisionHistoryAsync(id, 1); Assert.Single(history); Assert.Equal(1, history[0].Revision);
        }
        finally { TryDelete(path); }
    }

    [Fact(DisplayName = "T12 audit rows retain action and do not require document content")]
    public async Task T12_audit_persists_final_action_without_sensitive_payload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"final-audit-{Guid.NewGuid():N}.db");
        try { var repo = new SqliteWorkflowRepository(path); await repo.RecordAuditAsync(Audit(Guid.NewGuid(), DocumentAuditAction.AccessChanged)); }
        finally { TryDelete(path); }
    }
    private static ReviewSubmission Submission(Guid id) => new() { PackageId = id, Revision = 1, Status = WorkflowStatus.Draft, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(), ExtractionEvidenceJson = "{\"extraction\":true}", VerificationEvidenceJson = "{\"verification\":true}", MakerUserId = "maker", MakerDisplayName = "Maker", SubmittedUtc = DateTime.UtcNow, MakerAuthorized = true };
    private static ReviewDecision Decision(Guid id, ReviewDecisionType type) => new() { PackageId = id, Revision = 1, ApproverUserId = "approver", ApproverDisplayName = "Approver", CheckerUserId = "approver", CheckerDisplayName = "Approver", IsFinalApproval = true, DecisionType = type, SnapshotReference = "snapshot", SnapshotJson = "{\"reviewed\":true}", DecidedUtc = DateTime.UtcNow };
    private static AuditEntry Audit(Guid id, DocumentAuditAction action, string userId = "approver") => new() { AuditId = Guid.NewGuid(), DocumentId = id, Action = action, UserId = userId, TimestampUtc = DateTime.UtcNow, DetailsJson = "{\"package\":\"safe\"}" };
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
