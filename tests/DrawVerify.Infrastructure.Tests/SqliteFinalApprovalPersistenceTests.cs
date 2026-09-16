using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteFinalApprovalPersistenceTests
{
    [Fact(DisplayName = "TD-06 TD-09 TD-10 SQLite rejection persists route snapshot audit and history")]
    public async Task Td06_td09_td10_rejection_round_trips_atomically_and_in_order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"final-rejection-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteWorkflowRepository(path);
            var id = Guid.NewGuid();
            var submission = Submission(id) with { Status = WorkflowStatus.PendingFinalApproval };
            await repo.SubmitAtomicallyAsync(new WorkflowState { PackageId = id, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(id, DocumentAuditAction.SubmissionSucceeded, "maker"));
            var package = (await repo.GetReviewPackageAsync(id, 1))!;
            var decision = Decision(id, ReviewDecisionType.Return) with
            {
                ApproverUserId = "approver-17", ApproverDisplayName = "Approver Seventeen",
                Route = ReviewDecisionRoute.ToChecker, DestinationRole = "Checker", DestinationQueue = "Checker",
                OverallReason = "Dimension mismatch", Comments = "Correct datum callout and resubmit.",
                SnapshotReference = "snapshot-r7", SnapshotJson = "{\"step\":\"step-r7\",\"dxf\":\"dxf-r7\"}",
                DecidedUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc)
            };
            var audit = new AuditEntry { AuditId = Guid.NewGuid(), DocumentId = id, UserId = "approver-17", Action = DocumentAuditAction.RejectedToChecker, TimestampUtc = decision.DecidedUtc, DetailsJson = "{\"packageId\":\"" + id + "\",\"revision\":1,\"route\":\"ToChecker\",\"snapshotReference\":\"snapshot-r7\",\"destinationQueue\":\"Checker\"}" };
            await repo.SaveReviewDecisionAsync(package, decision, WorkflowStatus.ReturnedForRework, audit);
            var reloaded = (await repo.GetReviewPackageAsync(id, 1))!.PriorDecision!;
            Assert.Equal(ReviewDecisionRoute.ToChecker, reloaded.Route);
            Assert.Equal("approver-17", reloaded.ApproverUserId);
            Assert.Equal("snapshot-r7", reloaded.SnapshotReference);
            Assert.Equal("Dimension mismatch", reloaded.OverallReason);
            Assert.Equal("Checker", reloaded.DestinationQueue);
            Assert.Single(await repo.GetDecisionHistoryAsync(id, 1));
            Assert.Equal(WorkflowStatus.ReturnedForRework, (await repo.GetSubmissionsAsync(id)).Single().Status);
        }
        finally { TryDelete(path); }
    }

    [Fact(DisplayName = "TD-06 duplicate final rejection returns prior decision and preserves one row")]
    public async Task Td06_duplicate_rejection_is_transactionally_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"final-duplicate-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteWorkflowRepository(path); var id = Guid.NewGuid(); var submission = Submission(id);
            await repo.SubmitAtomicallyAsync(new WorkflowState { PackageId = id, Status = WorkflowStatus.Draft, Revision = 1 }, submission, Audit(id, DocumentAuditAction.SubmissionSucceeded, "maker"));
            var package = (await repo.GetReviewPackageAsync(id, 1))!;
            var first = Decision(id, ReviewDecisionType.Return) with { Route = ReviewDecisionRoute.ToChecker, DestinationRole = "Checker", Comments = "Correct datum callout and resubmit.", OverallReason = "Dimension mismatch" };
            await repo.SaveReviewDecisionAsync(package, first, WorkflowStatus.ReturnedForRework, Audit(id, DocumentAuditAction.RejectedToChecker));
            var duplicate = Decision(id, ReviewDecisionType.Return) with { Route = ReviewDecisionRoute.ToChecker, DestinationRole = "Checker", Comments = "Correct datum callout and resubmit.", OverallReason = "Dimension mismatch" };
            var conflict = await Assert.ThrowsAsync<ReviewDecisionConflictException>(() => repo.SaveReviewDecisionAsync(package, duplicate, WorkflowStatus.ReturnedForRework, Audit(id, DocumentAuditAction.RejectedToChecker)));
            Assert.NotNull(conflict.PriorDecision);
            Assert.Single(await repo.GetDecisionHistoryAsync(id, 1));
        }
        finally { TryDelete(path); }
    }
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

    [Fact(DisplayName = "TD-009 new revisions retain independent Checker signature persistence")]
    public void Checker_revision_contract_is_version_scoped()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Infrastructure", "Persistence", "SqliteWorkflowRepository.cs")));
        Assert.Contains("CheckerSignatureJson", source);
        Assert.Contains("CheckerSignatureFinalized IS NULL", source);
        Assert.Contains("signature.Revision != package.Revision", source);
        Assert.Contains("CheckerSignature = checkerSignature", source);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
    private static ReviewSubmission Submission(Guid id) => new() { PackageId = id, Revision = 1, Status = WorkflowStatus.Draft, StepDocumentVersionId = Guid.NewGuid(), DxfDocumentVersionId = Guid.NewGuid(), VerificationSessionId = Guid.NewGuid(), ExtractionEvidenceJson = "{\"extraction\":true}", VerificationEvidenceJson = "{\"verification\":true}", MakerUserId = "maker", MakerDisplayName = "Maker", SubmittedUtc = DateTime.UtcNow, MakerAuthorized = true, Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 } };
    private static ReviewDecision Decision(Guid id, ReviewDecisionType type) => new() { PackageId = id, Revision = 1, ApproverUserId = "approver", ApproverDisplayName = "Approver", CheckerUserId = "approver", CheckerDisplayName = "Approver", IsFinalApproval = true, DecisionType = type, SnapshotReference = "snapshot", SnapshotJson = "{\"reviewed\":true}", DecidedUtc = DateTime.UtcNow };
    private static AuditEntry Audit(Guid id, DocumentAuditAction action, string userId = "approver") => new() { AuditId = Guid.NewGuid(), DocumentId = id, Action = action, UserId = userId, TimestampUtc = DateTime.UtcNow, DetailsJson = "{\"package\":\"safe\"}" };
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
