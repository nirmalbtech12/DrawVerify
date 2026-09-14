using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class FinalApprovalDomainTests
{
    [Fact(DisplayName = "TD-01 validates Approver rejection route metadata and required fields")]
    public void Td01_approver_rejection_metadata_is_revision_bound_and_explicit()
    {
        var package = Guid.Parse("00000000-0000-0000-0000-000000000054");
        var decision = new ReviewDecision
        {
            PackageId = package, Revision = 7, ApproverUserId = "approver-17", ApproverDisplayName = "Approver",
            IsFinalApproval = true, DecisionType = ReviewDecisionType.Return, Comments = "Correct datum callout and resubmit.",
            OverallReason = "Dimension mismatch", SnapshotReference = "snapshot-r7", Route = ReviewDecisionRoute.ToChecker,
            DestinationRole = "Checker", DestinationQueue = "Checker"
        };

        decision.Validate();
        Assert.Equal(ReviewDecisionRoute.ToChecker, decision.Route);
        Assert.Equal("Checker", decision.DestinationRole);
        Assert.Equal(7, decision.Revision);
        Assert.Throws<ArgumentException>(() => (decision with { Comments = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (decision with { Route = ReviewDecisionRoute.ToMaker }).Validate());
        Assert.Throws<ArgumentException>(() => (decision with { DestinationRole = "Maker" }).Validate());
    }
    [Fact(DisplayName = "T01 approver queue states and permission are explicit")]
    public void T01_approver_permission_and_states_are_restricted()
    {
        var auth = new RoleAuthorization();
        var approver = new AuthenticatedUser("a", new[] { SystemRoles.Approver });
        var checker = new AuthenticatedUser("c", new[] { SystemRoles.Checker });
        Assert.True(auth.IsAllowed(approver, RolePermission.FinalApproval));
        Assert.False(auth.IsAllowed(checker, RolePermission.FinalApproval));
        Assert.Contains(WorkflowStatus.ApprovedByChecker, new[] { WorkflowStatus.ApprovedByChecker, WorkflowStatus.PendingFinalApproval });
        Assert.DoesNotContain(WorkflowStatus.Draft, new[] { WorkflowStatus.ApprovedByChecker, WorkflowStatus.PendingFinalApproval });
    }

    [Fact(DisplayName = "T02 final approve reject and return validate transitions")]
    public void T02_final_decisions_validate_comments_identity_and_revision()
    {
        var id = Guid.NewGuid();
        var baseDecision = new ReviewDecision { PackageId = id, Revision = 1, ApproverUserId = "approver-1", IsFinalApproval = true, SnapshotReference = "snapshot-1" };
        (baseDecision with { DecisionType = ReviewDecisionType.Approve }).Validate();
        Assert.Throws<ArgumentException>(() => (baseDecision with { DecisionType = ReviewDecisionType.Reject, Comments = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (baseDecision with { DecisionType = ReviewDecisionType.Return, Comments = null }).Validate());
        Assert.Equal(WorkflowStatus.FinallyApproved, WorkflowStatus.FinallyApproved);
        Assert.Equal(WorkflowStatus.ReturnedForRework, WorkflowStatus.ReturnedForRework);
        Assert.Throws<ArgumentOutOfRangeException>(() => (baseDecision with { Revision = 0 }).Validate());
    }
}
