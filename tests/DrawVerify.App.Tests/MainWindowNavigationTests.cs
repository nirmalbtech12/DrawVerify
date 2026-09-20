using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawVerify.App.Navigation;
using DrawVerify.App.Views;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DrawVerify.App.Tests;

public sealed class MainWindowNavigationTests
{
    [Fact(DisplayName = "TSD-DOCUMENT-LINK notification activation preserves exact package and revision")]
    public async Task TSD_DOCUMENT_LINK_activation_routes_to_exact_workflow_target()
    {
        var notification = Notification(); var service = new NotificationService(notification); var model = new NotificationCenterViewModel(service, new Session("maker-001")); var target = default((Guid PackageId, int Revision)?);
        model.ReviewRequested += (packageId, revision) => target = (packageId, revision);
        await model.LoadAsync(); await model.ActivateAsync(notification);
        Assert.Equal((notification.PackageId, notification.Revision), target); Assert.True(model.Notifications.Single().IsRead); Assert.Equal(string.Empty, model.Error);
    }

    [Fact(DisplayName = "TSD-INAPP-CENTER notification center shows unread document reference")]
    public async Task TSD_INAPP_CENTER_loads_unread_maker_notification()
    {
        var notification = Notification(); var model = new NotificationCenterViewModel(new NotificationService(notification), new Session("maker-001"));
        await model.LoadAsync(); var loaded = Assert.Single(model.Notifications);
        Assert.False(loaded.IsRead); Assert.Equal(notification.PackageId, loaded.PackageId); Assert.Equal(notification.Revision, loaded.Revision); Assert.Equal(notification.CreatedUtc, loaded.CreatedUtc);
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-READ read-state failure leaves notification unread and visible")]
    public async Task TSD_NOTIFICATION_READ_failure_does_not_claim_success()
    {
        var notification = Notification(); var service = new NotificationService(notification) { FailMarkRead = true }; var model = new NotificationCenterViewModel(service, new Session("maker-001")); await model.LoadAsync();
        await model.ActivateAsync(notification);
        Assert.False(model.Notifications.Single().IsRead); Assert.Contains("could not be opened", model.Error, StringComparison.OrdinalIgnoreCase); Assert.Empty(service.ReviewRequests);
    }

    private static InAppNotification Notification() => new() { NotificationId = Guid.NewGuid(), UserId = "maker-001", PackageId = Guid.Parse("20000000-0000-0000-0000-000000000001"), Revision = 4, Message = "Document rejected - action required", CreatedUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc), Target = "/workflow/20000000-0000-0000-0000-000000000001/4", EventType = "MakerNotificationSent", Channel = WorkflowNotificationChannel.InApp.ToString() };

    private sealed class Session(string userId) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser { get; } = new("maker", new[] { SystemRoles.Maker }) { UserId = userId, DisplayName = "Maker One" };
        public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser user) { } public void SignOut() { }
    }

    private sealed class NotificationService(InAppNotification notification) : INotificationService
    {
        public bool FailMarkRead { get; init; } public List<(Guid PackageId, int Revision)> ReviewRequests { get; } = new(); private InAppNotification _notification = notification;
        public Task<IReadOnlyList<InAppNotification>> GetForUserAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InAppNotification>>(_notification.UserId == userId ? new[] { _notification } : Array.Empty<InAppNotification>());
        public Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default) { if (FailMarkRead) return Task.FromException(new InvalidOperationException("read-state persistence failed")); _notification = _notification with { IsRead = true }; return Task.CompletedTask; }
    }
    [Fact(DisplayName = "T01 Maker menu renames only the combined upload entry")]
    public void Upload_verify_menu_target_is_registered()
    {
        Assert.Equal("Upload & Verify", ScreenRegistry.Title(ScreenRegistry.UploadVerify));
        var menu = Assert.Single(ScreenRegistry.Menus, item => item.Header == "_Upload & Verify");
        Assert.Equal(ScreenRegistry.UploadVerify, menu.Target);
        Assert.NotNull(menu.Children);
        Assert.Contains(menu.Children!, item => item.Target == ScreenRegistry.UploadVerify);
        Assert.DoesNotContain(menu.Children!, item => item.Target == ScreenRegistry.StepUpload);
        Assert.DoesNotContain(menu.Children!, item => item.Target == ScreenRegistry.DxfUpload);
        Assert.Contains(ScreenRegistry.Menus, item => item.Header == "_Verification");
    }

    [Fact(DisplayName = "WPF-001 authenticated shell keeps menu, content, title, and status regions")]
    public void ShellRegionsRemainMountedWhenScreensChange()
    {
        Sta.Run(() =>
        {
            var shell = new ShellView();
            var menu = Assert.IsType<Menu>(shell.FindName("ApplicationMenu"));
            var content = Assert.IsType<ContentControl>(shell.FindName("ContentHost"));
            Assert.NotNull(shell.FindName("StatusText"));
            shell.SetScreen(new TextBlock { Text = "Dashboard" }, ScreenRegistry.Dashboard);
            shell.SetScreen(new TextBlock { Text = "Verification" }, ScreenRegistry.Verification);
            Assert.Same(menu, shell.FindName("ApplicationMenu"));
            Assert.Same(content, shell.FindName("ContentHost"));
            Assert.Equal("Verification", Assert.IsType<TextBlock>(shell.FindName("ScreenTitle")).Text);
            Assert.Contains("Dashboard", Assert.IsType<TextBlock>(shell.FindName("Breadcrumb")).Text);
        });
    }

    [Fact(DisplayName = "WPF-001 task shell exposes readable title and secondary action region")]
    public void TaskShellHasTitleAndBackRegion()
    {
        Sta.Run(() =>
        {
            var shell = new ShellView();
            Assert.IsType<Button>(shell.FindName("BackButton"));
            shell.SetScreen(new TextBlock(), ScreenRegistry.StepUpload);
            Assert.Equal("Upload Model", Assert.IsType<TextBlock>(shell.FindName("ScreenTitle")).Text);
        });
    }

    [Fact(DisplayName = "MAN-001 representative shell structure is visually reviewable")]
    public void RepresentativeShellStructureHasReadableRegions()
    {
        Sta.Run(() =>
        {
            var shell = new ShellView();
            var menu = Assert.IsType<Menu>(shell.FindName("ApplicationMenu"));
            var content = Assert.IsType<ContentControl>(shell.FindName("ContentHost"));
            var title = Assert.IsType<TextBlock>(shell.FindName("ScreenTitle"));
            var breadcrumb = Assert.IsType<TextBlock>(shell.FindName("Breadcrumb"));
            var status = Assert.IsType<TextBlock>(shell.FindName("StatusText"));
            var back = Assert.IsType<Button>(shell.FindName("BackButton"));

            Assert.True(menu.IsVisible || !shell.IsLoaded);
            Assert.True(content.Visibility == Visibility.Visible);
            Assert.False(string.IsNullOrWhiteSpace(title.Text));
            Assert.False(string.IsNullOrWhiteSpace(breadcrumb.Text));
            Assert.False(string.IsNullOrWhiteSpace(status.Text));
            Assert.Equal("Back", back.Content?.ToString());
            Assert.True(back.HorizontalAlignment is HorizontalAlignment.Left or HorizontalAlignment.Stretch);
        });
    }
}

internal static class Sta
{
    private static readonly object Gate = new();

    public static void Run(Action action)
    {
        lock (Gate)
        {
            Exception? failure = null;
            using var completed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    EnsureApplicationResources();
                    action();
                }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); completed.Set(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "STA UI operation exceeded 5 seconds.");
            Assert.True(thread.Join(TimeSpan.FromSeconds(1)), "STA UI thread did not terminate within the cleanup bound.");
            if (failure is not null) throw new Xunit.Sdk.XunitException("STA UI assertion failed.", failure);
        }
    }

    private static void EnsureApplicationResources()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        if (application.Resources.Contains("PrimaryAction")) return;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DrawVerify.App;component/Resources/Styles.xaml", UriKind.Relative)
        });
    }
}
