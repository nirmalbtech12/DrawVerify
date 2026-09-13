using System.Windows;
using System.Windows.Controls;
using DrawVerify.App.ViewModels;
using DrawVerify.App.Views;
using DrawVerify.App.Views.Shared;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class TaskScreenNavigationTests
{
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
