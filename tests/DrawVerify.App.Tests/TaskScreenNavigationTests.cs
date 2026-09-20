using System.Windows;
using System.Windows.Controls;
using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;
using DrawVerify.App.Views.Shared;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class TaskScreenNavigationTests
{
    [Fact(DisplayName = "TSD-NOTIFICATION-READ task-screen notification activation uses durable service")]
    public async Task TSD_NOTIFICATION_READ_activation_marks_unread_item_read()
    {
        var notification = new InAppNotification { NotificationId = Guid.NewGuid(), UserId = "maker-001", PackageId = Guid.NewGuid(), Revision = 2, CreatedUtc = DateTime.UtcNow, Message = "rejected", IsRead = false };
        var service = new NotificationService(notification); var model = new NotificationCenterViewModel(service, new Session()); await model.LoadAsync(); await model.ActivateAsync(notification);
        Assert.True(model.Notifications.Single().IsRead); Assert.Equal(1, service.MarkReadCalls);
    }

    private sealed class Session : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = new("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001" }; public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser user) { } public void SignOut() { } }
    private sealed class NotificationService(InAppNotification notification) : INotificationService { private InAppNotification _notification = notification; public int MarkReadCalls { get; private set; } public Task<IReadOnlyList<InAppNotification>> GetForUserAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InAppNotification>>(new[] { _notification }); public Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default) { MarkReadCalls++; _notification = _notification with { IsRead = true }; return Task.CompletedTask; } }
    [Fact(DisplayName = "WPF-004 task screens expose Cancel and progress controls")]
    public void UploadScreensExposeSafeTaskControls()
    {
        Sta.Run(() =>
        {
            foreach (var view in new Control[] { new StepUploadView(), new DxfUploadView() })
            {
                view.Measure(new Size(1200, 800));
                view.Arrange(new Rect(0, 0, 1200, 800));
                var buttons = Find<Button>(view).ToArray();
                Assert.Contains(buttons, button => string.Equals(button.Content?.ToString(), "Cancel", StringComparison.OrdinalIgnoreCase));
                Assert.NotEmpty(Find<ProgressBar>(view));
            }
        });
    }

    [Fact(DisplayName = "WPF-004 clean and dirty task state is isolated per view model")]
    public void DirtyStateIsIndependent()
    {
        var first = new TestTask();
        var second = new TestTask();
        first.MakeDirty();
        Assert.True(first.IsDirty);
        Assert.False(second.IsDirty);
    }

    [Fact(DisplayName = "WPF-004 dirty task state can be cleared after confirmed discard")]
    public void ConfirmedDiscardClearsDirtyStateWithoutSharedState()
    {
        var task = new TestTask();
        task.MakeDirty();
        task.Discard();
        Assert.False(task.IsDirty);
    }

    [Fact(DisplayName = "WPF-006 empty verification state is explicit")]
    public void EmptyVerificationCollectionsHaveEmptyStateView()
    {
        using var model = new VerificationViewModel(new DrawVerify.Application.Services.SessionAuthenticationService(), new DrawVerify.Core.Authorization.RoleAuthorization());
        Assert.Empty(model.MatchResults);
        Sta.Run(() =>
        {
            var empty = new EmptyStateView();
            Assert.Contains("No items found", Find<TextBlock>(empty).Select(x => x.Text));
        });
    }

    [Fact(DisplayName = "WPF-007 progress status is observable without invented completion")]
    public void ProgressStatusIsIndeterminateUntilOperationCompletes()
    {
        Sta.Run(() =>
        {
            var status = new ProgressStatusView();
            Assert.True(Assert.Single(Find<ProgressBar>(status)).IsIndeterminate);
            Assert.Contains("Operation in progress\u2026", Find<TextBlock>(status).Select(x => x.Text));
        });
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class TestTask : TaskScreenViewModel
    {
        public void MakeDirty() => MarkDirty();
        public void Discard() => IsDirty = false;
    }
}
