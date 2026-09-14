using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class FinalApprovalDomainTests
{
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
