using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Authorization;
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

    [Fact(DisplayName = "TD-UI-01 successful Maker submission confirms and navigates")]
    public async Task Successful_submission_shows_confirmation_and_navigation()
    {
        var service = new FakeWorkflowService();
        var auth = new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" };
        var viewModel = new WorkflowViewModel(service, new FakeAuthentication(auth));
        var navigated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.NavigationRequested += value => navigated.TrySetResult(value);
        viewModel.BeginSubmission(Guid.NewGuid());
        await WaitForAsync(() => viewModel.IsConfirmationVisible, TimeSpan.FromSeconds(5));
        viewModel.Note = "Please review hole tolerances";

        viewModel.SubmitCommand.Execute(null);

        Assert.Equal("Submitted", await navigated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("The drawing was submitted for review.", viewModel.Message);
        Assert.Equal(1, service.SubmissionCalls);
        Assert.False(viewModel.IsConfirmationVisible);
    }

    [Fact(DisplayName = "TD-UI-02 non-Maker cannot submit and readiness errors are displayed")]
    public async Task Non_maker_command_is_disabled_and_failed_submission_is_visible()
    {
        var service = new FakeWorkflowService { Result = new SubmissionResult(false, "DXF file version is not available.", Failures: new[] { "DXF file version is not available." }) };
        var auth = new AuthenticatedUser("checker", new[] { SystemRoles.Checker });
        var viewModel = new WorkflowViewModel(service, new FakeAuthentication(auth));
        viewModel.BeginSubmission(Guid.NewGuid());
        await WaitForAsync(() => viewModel.IsConfirmationVisible, TimeSpan.FromSeconds(5));

        Assert.False(viewModel.SubmitCommand.CanExecute(null));
        Assert.Equal(0, service.SubmissionCalls);

        var failedService = new FakeWorkflowService { Result = new SubmissionResult(false, "DXF file version is not available.", Failures: new[] { "DXF file version is not available." }) };
        var maker = new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" };
        var failedViewModel = new WorkflowViewModel(failedService, new FakeAuthentication(maker));
        failedViewModel.BeginSubmission(Guid.NewGuid());
        await WaitForAsync(() => failedViewModel.IsConfirmationVisible, TimeSpan.FromSeconds(5));
        failedViewModel.SubmitCommand.Execute(null);
        await WaitForAsync(() => !string.IsNullOrEmpty(failedViewModel.Message), TimeSpan.FromSeconds(5));

        Assert.Contains("DXF", failedViewModel.Message);
        Assert.Equal(1, failedService.SubmissionCalls);
        Assert.True(failedViewModel.IsConfirmationVisible);
    }

    [Fact(DisplayName = "TD-UI-02 persistence failure is visible and does not navigate")]
    public async Task Failed_submission_does_not_show_success_or_navigate()
    {
        var service = new FakeWorkflowService { ThrowOnSubmit = true };
        var maker = new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" };
        var viewModel = new WorkflowViewModel(service, new FakeAuthentication(maker));
        var navigated = false; viewModel.NavigationRequested += _ => navigated = true;
        viewModel.BeginSubmission(Guid.NewGuid());
        await WaitForAsync(() => viewModel.IsConfirmationVisible, TimeSpan.FromSeconds(5));
        viewModel.SubmitCommand.Execute(null);
        await WaitForAsync(() => !string.IsNullOrEmpty(viewModel.Message), TimeSpan.FromSeconds(5));
        Assert.Contains("could not be submitted", viewModel.Message);
        Assert.False(navigated); Assert.True(viewModel.IsConfirmationVisible);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for ViewModel state.");
            await Task.Delay(10);
        }
    }
}

file sealed class FakeWorkflowService : IWorkflowService
{
    public int CancellationCalls { get; private set; }
    public int SubmissionCalls { get; private set; }
    public SubmissionResult Result { get; set; } = new(true, "The drawing was submitted for review.");
    public bool ThrowOnSubmit { get; set; }
    public Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult(SubmissionReadiness.Ready);
    public Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default) { SubmissionCalls++; if (ThrowOnSubmit) throw new InvalidOperationException("persist failed"); return Task.FromResult(Result); }
    public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) { CancellationCalls++; return Task.CompletedTask; }
    public Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(null);
    public Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
}

file sealed class FakeAuthentication(AuthenticatedUser? user) : ISessionAuthenticationService
{
    public AuthenticatedUser? CurrentUser => user;
    public bool IsAuthenticated => user is not null;
    public void SignIn(AuthenticatedUser authenticatedUser) => throw new NotSupportedException();
    public void SignOut() => throw new NotSupportedException();
}
