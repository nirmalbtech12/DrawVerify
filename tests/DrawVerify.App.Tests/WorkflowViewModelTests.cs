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
        Assert.Equal(0, service.SubmissionCalls);
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

    [Fact(DisplayName = "TD-UI-02 duplicate workflow status is shown without success")]
    public async Task Duplicate_submission_displays_current_status()
    {
        var service = new FakeWorkflowService { Result = new SubmissionResult(false, "The package is already Under Review.", Failures: new[] { "The package is already Under Review." }) };
        var maker = new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" };
        var viewModel = new WorkflowViewModel(service, new FakeAuthentication(maker));
        viewModel.BeginSubmission(Guid.NewGuid());
        await WaitForAsync(() => viewModel.IsConfirmationVisible, TimeSpan.FromSeconds(5));

        viewModel.SubmitCommand.Execute(null);
        await WaitForAsync(() => !string.IsNullOrEmpty(viewModel.Message), TimeSpan.FromSeconds(5));

        Assert.Contains("Under Review", viewModel.Message);
        Assert.True(viewModel.IsConfirmationVisible);
        Assert.Equal(1, service.SubmissionCalls);
    }

    [Fact(DisplayName = "TD-APPUI-049 Checker can open the complete review package")]
    public async Task Checker_review_projection_contains_all_submission_sections()
    {
        var service = new FakeWorkflowService { ReviewPackage = ReviewFixture() };
        var checker = new AuthenticatedUser("checker", new[] { SystemRoles.Checker });
        var model = new WorkflowViewModel(service, new FakeAuthentication(checker), new RoleAuthorization());
        await model.OpenReviewAsync(new WorkflowQueueItem(service.ReviewPackage.PackageId, 7, "Maker One", service.ReviewPackage.SubmittedUtc, WorkflowStatus.SubmittedForReview, "missing D1"));
        Assert.True(model.IsReviewMode); Assert.Equal(service.ReviewPackage.StepDocumentVersionId, model.ReviewPackage!.StepDocumentVersionId); Assert.Equal(service.ReviewPackage.DxfDocumentVersionId, model.ReviewPackage.DxfDocumentVersionId); Assert.Equal("{\"dimensions\":[10]}", model.ReviewPackage.ExtractionEvidenceJson); Assert.Equal("Maker One", model.ReviewPackage.MakerDisplayName);
    }

    [Fact(DisplayName = "TD-APPUI-050 review commands require comments and navigate findings")]
    public async Task Review_commands_validate_comments_and_execute_decision()
    {
        var service = new FakeWorkflowService { ReviewPackage = ReviewFixture() }; var model = new WorkflowViewModel(service);
        await model.OpenReviewAsync(new WorkflowQueueItem(service.ReviewPackage.PackageId, 7, "Maker One", DateTime.UtcNow, WorkflowStatus.SubmittedForReview, "missing"));
        Assert.True(model.ApproveCommand.CanExecute(null)); Assert.False(model.RejectCommand.CanExecute(null)); Assert.False(model.RequestChangesCommand.CanExecute(null));
        model.ReviewComment = "  fix control D1  "; Assert.True(model.RejectCommand.CanExecute(null)); model.RejectCommand.Execute(null); await WaitForAsync(() => service.DecisionCalls == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(ReviewDecisionType.Reject, service.LastDecisionType); Assert.Equal("fix control D1", service.LastComments);
        var finding = Assert.Single(service.ReviewPackage.VerificationFindings);
        var navigated = default(ReviewFinding);
        model.VerificationNavigationRequested += value => navigated = value;
        model.SelectVerificationFinding(finding);
        Assert.Equal("D1", navigated!.HighlightTarget);
    }

    [Fact(DisplayName = "TD-APPUI-050 confirmed safeguard permits decision after key sections are viewed")]
    public async Task Confirmed_review_safeguard_does_not_block_decision()
    {
        var service = new FakeWorkflowService { ReviewPackage = ReviewFixture() };
        var model = new WorkflowViewModel(service) { RequireSectionConfirmation = true };
        await model.OpenReviewAsync(new WorkflowQueueItem(service.ReviewPackage.PackageId, 7, "Maker One", DateTime.UtcNow, WorkflowStatus.SubmittedForReview, "verified"));
        var prompts = 0;
        model.DecisionConfirmationRequested += () => prompts++;

        model.ApproveCommand.Execute(null);
        Assert.Equal(1, prompts);
        Assert.Equal(0, service.DecisionCalls);

        model.ConfirmDecision();
        await WaitForAsync(() => service.DecisionCalls == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(ReviewDecisionType.Approve, service.LastDecisionType);
    }

    [Fact(DisplayName = "TD-APPUI-051 comment-only save and duplicate feedback remain visible")]
    public async Task Comment_save_does_not_change_review_status_and_duplicate_is_visible()
    {
        var service = new FakeWorkflowService { ReviewPackage = ReviewFixture() }; var model = new WorkflowViewModel(service);
        await model.OpenReviewAsync(new WorkflowQueueItem(service.ReviewPackage.PackageId, 7, "Maker One", DateTime.UtcNow, WorkflowStatus.SubmittedForReview, "verified"));
        model.ReviewComment = "looks good"; model.SaveReviewCommentCommand.Execute(null); await WaitForAsync(() => service.CommentCalls == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(WorkflowStatus.SubmittedForReview, service.ReviewPackage.Status); Assert.Equal(0, service.DecisionCalls);
        service.DecideException = new InvalidOperationException("The review decision was not saved; refresh before deciding."); model.ApproveCommand.Execute(null); await WaitForAsync(() => !string.IsNullOrEmpty(model.ReviewError), TimeSpan.FromSeconds(5)); Assert.Contains("decision was not saved", model.ReviewError, StringComparison.OrdinalIgnoreCase); Assert.Contains("try again", model.ReviewError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-APPUI-049 unauthorized review queue failure is visible")]
    public async Task Unauthorized_review_queue_is_not_presented_as_success()
    {
        var service = new FakeWorkflowService { ThrowOnQueue = true };
        var model = new WorkflowViewModel(service);

        await model.LoadQueueAsync();

        Assert.Empty(model.Queue);
        Assert.Contains("Only authorized reviewers", model.ReviewError);
    }

    [Fact(DisplayName = "TD-07 Maker rejection view exposes metadata and correction responses")]
    public async Task Maker_rejection_context_exposes_all_required_fields()
    {
        var packageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var prior = new ReviewComment { PackageId = packageId, Revision = 1, AuthorUserId = "checker-001", AuthorDisplayName = "Checker One", Text = "Fix dimension 4", ReferenceType = "dimension", ReferenceId = "DIM-4", IsDraft = false };
        var response = new ReviewComment { PackageId = packageId, Revision = 2, AuthorUserId = "maker-001", AuthorDisplayName = "Maker One", Text = "Corrected dimension 4", PriorCommentId = prior.CommentId, CorrectionResponse = "Tolerance corrected", IsDraft = false };
        var rejected = ReviewPackage.FromSubmission(new ReviewSubmission { PackageId = packageId, Revision = 1, MakerUserId = "maker-001", MakerDisplayName = "Maker One", Status = WorkflowStatus.Rejected, SubmittedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, new ReviewDecision { PackageId = packageId, Revision = 1, CheckerUserId = "checker-001", CheckerDisplayName = "Checker One", DecidedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), DecisionType = ReviewDecisionType.Reject, Comments = "Dimension 4 is outside tolerance", OverallReason = "Dimension 4 is outside tolerance", CorrectionAction = "Correct dimension 4 and resubmit" }, new[] { prior });
        var resubmitted = ReviewPackage.FromSubmission(new ReviewSubmission { PackageId = packageId, Revision = 2, MakerUserId = "maker-001", MakerDisplayName = "Maker One", Status = WorkflowStatus.SubmittedForReview }, rejected.PriorDecision, new[] { prior, response });
        var service = new FakeWorkflowService { ReviewPackage = resubmitted };
        var model = new WorkflowViewModel(service, new FakeAuthentication(new AuthenticatedUser("maker", new[] { SystemRoles.Maker })));
        await model.OpenReviewAsync(new WorkflowQueueItem(packageId, 2, "Maker One", DateTime.UtcNow, WorkflowStatus.SubmittedForReview, "corrected"));
        Assert.NotNull(model.ReviewPackage);
        var loaded = model.ReviewPackage!;
        Assert.Equal(WorkflowStatus.SubmittedForReview, loaded.Status);
        Assert.Equal("Checker One", loaded.PriorDecision!.CheckerDisplayName);
        Assert.Equal("Dimension 4 is outside tolerance", loaded.RejectionReason);
        Assert.Equal("Correct dimension 4 and resubmit", loaded.CorrectionAction);
        Assert.Equal("dimension", loaded.Comments.Single(x => x.PriorCommentId is null).ReferenceType);
        Assert.Equal("Tolerance corrected", loaded.Comments.Single(x => x.PriorCommentId == prior.CommentId).CorrectionResponse);
    }

    private static ReviewPackage ReviewFixture() => ReviewPackage.FromSubmission(new ReviewSubmission
    {
        PackageId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Revision = 7,
        StepDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000011"), DxfDocumentVersionId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
        ExtractionEvidenceJson = "{\"dimensions\":[10]}", VerificationEvidenceJson = "{\"missing\":[\"D1\"],\"findings\":[{\"identifier\":\"D1\",\"category\":\"Missing dimension\",\"highlightTarget\":\"D1\"}]}", MakerUserId = "maker-1", MakerDisplayName = "Maker One", SubmittedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    });

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
    public ReviewPackage? ReviewPackage { get; set; }
    public int DecisionCalls { get; private set; }
    public int CommentCalls { get; private set; }
    public ReviewDecisionType LastDecisionType { get; private set; }
    public string? LastComments { get; private set; }
    public Exception? DecideException { get; set; }
    public bool ThrowOnQueue { get; set; }
    public Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult(SubmissionReadiness.Ready);
    public Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default) { SubmissionCalls++; if (ThrowOnSubmit) throw new InvalidOperationException("persist failed"); return Task.FromResult(Result); }
    public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) { CancellationCalls++; return Task.CompletedTask; }
    public Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(null);
    public Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
    public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => ThrowOnQueue ? Task.FromException<IReadOnlyList<WorkflowQueueItem>>(new UnauthorizedAccessException()) : Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
    public Task<ReviewPackage?> OpenReviewPackageAsync(Guid packageId, int revision, CancellationToken cancellationToken = default) => Task.FromResult(ReviewPackage);
    public Task<ReviewDecision> DecideAsync(Guid packageId, int revision, ReviewDecisionType decisionType, string? comments, CancellationToken cancellationToken = default) { DecisionCalls++; if (DecideException is not null) return Task.FromException<ReviewDecision>(DecideException); LastDecisionType = decisionType; LastComments = comments?.Trim(); return Task.FromResult(new ReviewDecision { PackageId = packageId, Revision = revision, DecisionType = decisionType, CheckerUserId = "checker-1" }); }
    public Task<ReviewComment> AddReviewCommentAsync(Guid packageId, int revision, string text, CancellationToken cancellationToken = default) { CommentCalls++; return Task.FromResult(new ReviewComment { PackageId = packageId, Revision = revision, AuthorUserId = "checker-1", Text = text.Trim() }); }
}

file sealed class FakeAuthentication(AuthenticatedUser? user) : ISessionAuthenticationService
{
    public AuthenticatedUser? CurrentUser => user;
    public bool IsAuthenticated => user is not null;
    public void SignIn(AuthenticatedUser authenticatedUser) => throw new NotSupportedException();
    public void SignOut() => throw new NotSupportedException();
}
