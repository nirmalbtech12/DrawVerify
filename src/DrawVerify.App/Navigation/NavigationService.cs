using System.Windows;
using DrawVerify.Infrastructure.Logging;

namespace DrawVerify.App.Navigation;

public sealed class NavigationService(ScreenAccessGuard guard, NavigationHistory history, Action<UIElement, string> show, Action<string> denied)
{
    public string CurrentTarget { get; private set; } = ScreenRegistry.Dashboard;
    public bool Navigate(string target, Func<UIElement> factory, bool recordHistory = true)
    {
        if (!guard.CanAccess(target)) { Deny(target, "navigation"); return false; }
        try { if (recordHistory && CurrentTarget != target) history.Push(CurrentTarget); show(factory(), target); CurrentTarget = target; return true; }
        catch (Exception ex) { new CentralizedLogger().Log(ex, "WPF navigation", $"Unable to navigate to target '{target}'."); denied("Unable to open the requested screen."); return false; }
    }
    public bool GoBack(Func<string, UIElement> factory)
    {
        var target = history.TryPop();
        if (target is null || !guard.CanAccess(target)) { CurrentTarget = ScreenRegistry.Dashboard; return Navigate(ScreenRegistry.Dashboard, () => factory(ScreenRegistry.Dashboard), false); }
        return Navigate(target, () => factory(target), false);
    }
    public void Clear() => history.Clear();
    private void Deny(string target, string path) { new CentralizedLogger().Log(new UnauthorizedAccessException($"Access denied for '{target}'."), "WPF navigation", $"target={target}; entryPath={path};"); denied("You do not have permission to access this screen."); }
}
