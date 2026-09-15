using System.Windows;
using System.Windows.Controls;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class WorkflowConfigurationViewModelTests
{
    [Fact(DisplayName = "TD-UI-001 Admin workflow configuration view exposes editing and preview commands")]
    public void Admin_view_model_is_editable_and_view_renders_sections()
    {
        Sta.Run(() =>
        {
            var session = new Session(new("admin", [SystemRoles.Admin]));
            var vm = new WorkflowConfigurationViewModel(new Service(), session, new RoleAuthorization());
            var view = new DrawVerify.App.Views.WorkflowConfigurationView();
            Assert.True(vm.CanEdit); Assert.True(vm.SaveCommand.CanExecute(null)); Assert.True(vm.PreviewCommand.CanExecute(null));
            Assert.IsType<ScrollViewer>(view.Content);
        });
    }

    private sealed class Session(AuthenticatedUser user) : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = user; public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser value) { } public void SignOut() { } }
    private sealed class Service : IWorkflowConfigurationService
    {
        public Task<WorkflowConfiguration> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Baseline());
        public Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, int? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult(configuration with { Version = configuration.Version + 1 });
        public Task<WorkflowPreviewResult> PreviewAsync(WorkflowConfiguration configuration, WorkflowPreviewMetadata metadata, CancellationToken cancellationToken = default) => Task.FromResult(new WorkflowPreviewResult([], [], [], true));
        private static WorkflowConfiguration Baseline() => new() { PermittedInitialStates = [WorkflowStatus.Draft, WorkflowStatus.InPreparation], DuplicateSubmissionStates = [WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved] };
    }
}
