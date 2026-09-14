using DrawVerify.App.Navigation;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using DrawVerify.App.ViewModels;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class FinalApprovalViewModelTests
{
    [Fact(DisplayName = "TD-04 Approver rejection command requires reason and comments and is role gated")]
    public async Task Td04_approver_rejection_command_is_role_gated_and_validated()
    {
        var service = new FakeWorkflowService { ReviewPackage = ReviewPackage.FromSubmission(new ReviewSubmission { PackageId = Guid.NewGuid(), Revision = 7, Status = WorkflowStatus.PendingFinalApproval, VerificationEvidenceJson = "{}", ExtractionEvidenceJson = "{}" }) };
        var approver = new AuthenticatedUser("approver", new[] { SystemRoles.Approver });
        var model = new WorkflowViewModel(service, new Session(approver), new RoleAuthorization());
        await model.OpenReviewAsync(new WorkflowQueueItem(service.ReviewPackage!.PackageId, 7, "Maker", DateTime.UtcNow, WorkflowStatus.PendingFinalApproval, "summary"));
        Assert.False(model.RejectToCheckerCommand.CanExecute(null));
        model.ApproverRejectionReason = "Dimension mismatch";
        model.ReviewComment = "Correct datum callout and resubmit.";
        Assert.True(model.RejectToCheckerCommand.CanExecute(null));
        var viewer = new WorkflowViewModel(service, new Session(new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer })), new RoleAuthorization());
        await viewer.OpenReviewAsync(new WorkflowQueueItem(service.ReviewPackage.PackageId, 7, "Maker", DateTime.UtcNow, WorkflowStatus.PendingFinalApproval, "summary"));
        viewer.ApproverRejectionReason = "reason"; viewer.ReviewComment = "comments";
        Assert.False(viewer.RejectToCheckerCommand.CanExecute(null));
    }
    [Fact(DisplayName = "T08 T11 T14 role-aware approval navigation and denial")]
    public void T08_T11_T14_approver_is_allowed_and_other_roles_are_denied()
    {
        var authorization = new RoleAuthorization();
        Assert.True(new ScreenAccessGuard(new Session(new AuthenticatedUser("a", new[] { SystemRoles.Approver })), authorization).CanAccess(ScreenRegistry.Workflow));
        Assert.False(new ScreenAccessGuard(new Session(new AuthenticatedUser("v", new[] { SystemRoles.Viewer })), authorization).CanAccess(ScreenRegistry.Workflow));
        Assert.False(new ScreenAccessGuard(new Session(null), authorization).CanAccess(ScreenRegistry.Workflow));
    }
    private sealed class Session(AuthenticatedUser? user) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser { get; } = user; public bool IsAuthenticated => CurrentUser is not null; public void SignIn(AuthenticatedUser value) { } public void SignOut() { }
    }
}
