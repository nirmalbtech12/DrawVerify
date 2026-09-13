using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class WorkflowViewModelTests
{
    [Fact(DisplayName = "TD-UI-01 Maker confirmation preserves note and exposes status")]
    public void Confirmation_state_preserves_note_and_status_projection()
    {
        var service = new FakeWorkflowService(); var viewModel = new WorkflowViewModel(service); var package = Guid.NewGuid();
        viewModel.Note = "Please review hole tolerances"; viewModel.BeginSubmission(package);
        Assert.True(viewModel.IsConfirmationVisible); Assert.Equal(package, viewModel.PackageId); Assert.Equal("Please review hole tolerances", viewModel.Note);
        viewModel.Queue.Add(new WorkflowQueueItem(package, 1, "Maker One", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), WorkflowStatus.SubmittedForReview, "verified"));
        Assert.Equal(WorkflowStatus.SubmittedForReview, viewModel.Queue.Single().Status); Assert.Equal("Maker One", viewModel.Queue.Single().MakerDisplayName);
        viewModel.CancelCommand.Execute(null); Assert.False(viewModel.IsConfirmationVisible); Assert.Equal("Please review hole tolerances", viewModel.Note); Assert.Equal(1, service.CancellationCalls);
    }

    [Fact(DisplayName = "TD-UI-02 queue displays required Checker fields")]
    public void Queue_projection_contains_identifier_revision_maker_time_status_and_summary()
    {
        var viewModel = new WorkflowViewModel(); var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); var id = Guid.NewGuid();
        viewModel.Queue.Add(new WorkflowQueueItem(id, 1, "Maker One", timestamp, WorkflowStatus.SubmittedForReview, "2 matched dimensions"));
        var item = Assert.Single(viewModel.Queue); Assert.Equal(id, item.PackageId); Assert.Equal(1, item.Revision); Assert.Equal("Maker One", item.MakerDisplayName); Assert.Equal(timestamp, item.SubmittedUtc); Assert.Equal("2 matched dimensions", item.VerificationSummary);
    }
}

file sealed class FakeWorkflowService : IWorkflowService
{
    public int CancellationCalls { get; private set; }
    public Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult(SubmissionReadiness.Ready);
    public Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new SubmissionResult(true, "submitted"));
    public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) { CancellationCalls++; return Task.CompletedTask; }
    public Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(null);
    public Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
}
