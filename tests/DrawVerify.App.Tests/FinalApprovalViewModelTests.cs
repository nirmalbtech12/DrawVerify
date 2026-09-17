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
    [Fact(DisplayName = "REV-KAN66-001 Approver finalization refreshes persisted signature and approval command state")]
    public void Approver_finalization_contract_is_not_checker_bound()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "WorkflowView.xaml")));
        var codeBehind = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "WorkflowView.xaml.cs")));
        var viewModel = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "ViewModels", "WorkflowViewModel.cs")));
        Assert.Contains("ApproverSignature", viewModel);
        Assert.Contains("SetApproverSignatureAsync", viewModel);
        Assert.Contains("ApproverSignatureFinalizationRequested", codeBehind);
        Assert.Contains("FinalizedSignature=\"{Binding ApproverSignature", xaml);
        Assert.Contains("IsApprover=\"{Binding IsApprover}\"", xaml);
        Assert.DoesNotContain("ApproverSignatureFinalizationRequested\"", xaml.Replace("SignatureFinalizationRequested=\"ApproverSignatureFinalizationRequested\"", string.Empty));
    }

    [Fact(DisplayName = "TD-UI-02 persisted Approver signature gates approval and renders only for final approval")]
    public void View_model_contract_binds_current_revision_signature_and_lock_state()
    {
        var viewModel = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "ViewModels", "WorkflowViewModel.cs")));
        var view = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "WorkflowView.xaml")));
        Assert.Contains("ApproverSignature", viewModel);
        Assert.Contains("ApplyApproverSignatureAsync", viewModel);
        Assert.Contains("IsApprover", viewModel);
        Assert.Contains("ReviewError", viewModel);
        Assert.Contains("Approver Signature", view);
        Assert.Contains("ApproveCommand", viewModel);
        Assert.Contains("RaiseReviewCommands", viewModel);
        Assert.Contains("Revision", viewModel);
    }

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
}
