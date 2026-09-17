using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalApprovalStampViewModelTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T09 workflow UI exposes approval errors and preview stamp state")]
    public void T09_workflow_and_preview_models_expose_stamp_state()
    {
        using var viewModel = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        var stamp = Stamp();
        viewModel.StepPreview.SetInternalApprovalStamp(stamp);
        viewModel.DxfPreview.SetInternalApprovalStamp(stamp);
        Assert.True(viewModel.StepPreview.HasInternalApprovalStamp);
        Assert.True(viewModel.DxfPreview.HasInternalApprovalStamp);
        Assert.Contains(stamp.StampId.ToString("D"), viewModel.StepPreview.ApprovalStampText);
        Assert.Contains(stamp.StampId.ToString("D"), viewModel.DxfPreview.ApprovalStampText);
    }

    [Fact(DisplayName = "T10 preview views load and expose persisted stamp metadata")]
    public async Task T10_load_review_context_projects_stamp_to_both_previews()
    {
        using var viewModel = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        var package = ReviewPackage.FromSubmission(new ReviewSubmission
        {
            PackageId = Document, Revision = 1, Status = WorkflowStatus.FinallyApproved,
            VerificationSessionId = Guid.NewGuid(), InternalApprovalStamp = Stamp()
        });
        await viewModel.LoadReviewContextAsync(package, null);
        Assert.Same(package.Submission.InternalApprovalStamp, viewModel.StepPreview.InternalApprovalStamp);
        Assert.Same(package.Submission.InternalApprovalStamp, viewModel.DxfPreview.InternalApprovalStamp);
        foreach (var text in new[] { "Approver One", "2026-01-15", "1", "fingerprint-001", "Valid" })
        {
            Assert.Contains(text, viewModel.StepPreview.ApprovalStampText);
            Assert.Contains(text, viewModel.DxfPreview.ApprovalStampText);
        }
    }

    private static InternalApprovalStamp Stamp() => InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
}
