using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawVerify.App.Navigation;
using DrawVerify.App.Views;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DrawVerify.App.Tests;

public sealed class MainWindowNavigationTests
{
    [Fact(DisplayName = "T01 Maker menu renames only the combined upload entry")]
    public void Upload_verify_menu_target_is_registered()
    {
        Assert.Equal("Upload & Verify", ScreenRegistry.Title(ScreenRegistry.UploadVerify));
        var menu = Assert.Single(ScreenRegistry.Menus, item => item.Header == "_Upload & Verify");
        Assert.Equal(ScreenRegistry.UploadVerify, menu.Target);
        Assert.NotNull(menu.Children);
        Assert.Contains(menu.Children!, item => item.Target == ScreenRegistry.StepUpload);
        Assert.Contains(menu.Children!, item => item.Target == ScreenRegistry.DxfUpload);
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
