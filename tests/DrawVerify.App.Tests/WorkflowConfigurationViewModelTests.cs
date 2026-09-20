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
    [Fact(DisplayName = "TSD-NOTIFICATION-PREFERENCES Maker rejection preference preserves enabled InApp rule")]
    public void TSD_NOTIFICATION_PREFERENCES_maker_rejection_rule_is_visible()
    {
        Sta.Run(() =>
        {
            var configuration = Service.BaselineConfiguration() with { Notifications = [new() { Event = WorkflowNotificationEvent.Rejection, Enabled = true, RecipientRoles = ["Maker"], Channels = [WorkflowNotificationChannel.InApp], ContentTemplate = "rejected" }] };
            var vm = new WorkflowConfigurationViewModel(new Service(configuration), new Session(new("admin", [SystemRoles.Admin])), new RoleAuthorization());
            var rule = Assert.Single(vm.Notifications, notification => notification.Event == WorkflowNotificationEvent.Rejection);
            Assert.True(rule.Enabled); Assert.Contains(WorkflowNotificationChannel.InApp, rule.Channels); Assert.Contains("Maker", rule.RecipientRoles);
        });
    }
    [Fact(DisplayName = "TSD-AC06-PREFS approval notification rule exposes supported InApp configuration")]
    public void TSD_AC06_PREFS_supported_approval_rule_is_preserved()
    {
        Sta.Run(() =>
        {
            var configuration = Service.BaselineConfiguration() with
            {
                Notifications = [new() { Event = WorkflowNotificationEvent.Approval, Enabled = true, Channels = [WorkflowNotificationChannel.InApp] }]
            };
            var vm = new WorkflowConfigurationViewModel(new Service(configuration), new Session(new("admin", [SystemRoles.Admin])), new RoleAuthorization());
            Assert.Contains(vm.Notifications, notification => notification.Event == WorkflowNotificationEvent.Approval);
            Assert.Contains(vm.Notifications.Single(notification => notification.Event == WorkflowNotificationEvent.Approval).Channels, channel => channel == WorkflowNotificationChannel.InApp);
        });
    }
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
            var root = (ScrollViewer)view.Content;
            var panel = Assert.IsType<StackPanel>(root.Content);
            Assert.Contains(panel.Children.OfType<GroupBox>(), box => box.Header?.ToString() == "Role definitions");
            Assert.Contains(panel.Children.OfType<GroupBox>(), box => box.Header?.ToString()!.Contains("Notifications", StringComparison.Ordinal) == true);
        });
    }

    [Fact(DisplayName = "TD-UI-001 non-Admin editing commands are disabled and custom labels bind")]
    public void Non_admin_cannot_edit_and_custom_labels_are_visible()
    {
        Sta.Run(() =>
        {
            var configuration = Service.BaselineConfiguration() with
            {
                Roles = [new() { DisplayName = "Reviewer", SystemRole = SystemRoles.Checker }],
                StatusLabels = new Dictionary<WorkflowStatus, string> { [WorkflowStatus.UnderReview] = "In Review" }
            };
            var vm = new WorkflowConfigurationViewModel(new Service(configuration), new Session(new("maker", [SystemRoles.Maker])), new RoleAuthorization());
            Assert.False(vm.CanEdit);
            Assert.False(vm.SaveCommand.CanExecute(null));
            Assert.False(vm.PreviewCommand.CanExecute(null));
            Assert.Equal("Reviewer (Checker)", vm.RolesSummary);
            Assert.Contains(vm.StatusLabels, label => label.Value == "In Review");
        });
    }

    private sealed class Session(AuthenticatedUser? user) : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = user; public bool IsAuthenticated => CurrentUser is not null; public void SignIn(AuthenticatedUser value) { } public void SignOut() { } }
    private sealed class Service : IWorkflowConfigurationService
    {
        private readonly WorkflowConfiguration _configuration;
        public Service() : this(BaselineConfiguration()) { }
        public Service(WorkflowConfiguration configuration) => _configuration = configuration;
        public Task<WorkflowConfiguration> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_configuration);
        public Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, int? expectedVersion = null, CancellationToken cancellationToken = default) => Task.FromResult(configuration with { Version = configuration.Version + 1 });
        public Task<WorkflowPreviewResult> PreviewAsync(WorkflowConfiguration configuration, WorkflowPreviewMetadata metadata, CancellationToken cancellationToken = default) => Task.FromResult(new WorkflowPreviewResult([], [], [], true));
        internal static WorkflowConfiguration BaselineConfiguration() => new() { PermittedInitialStates = [WorkflowStatus.Draft, WorkflowStatus.InPreparation], DuplicateSubmissionStates = [WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved] };
    }
}
