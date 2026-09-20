using System.Windows;
using DrawVerify.Infrastructure.Logging;

namespace DrawVerify.App.Navigation;

public sealed class NavigationService(ScreenAccessGuard guard, NavigationHistory history, Action<UIElement, string> show, Action<string> denied)
{
    private string? _pendingSharedLinkToken;
    public string CurrentTarget { get; private set; } = ScreenRegistry.Dashboard;
    public bool BeginSharedLink(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        _pendingSharedLinkToken = token;
        return true;
    }
    public string? ConsumeSharedLink()
    {
        var token = _pendingSharedLinkToken;
        _pendingSharedLinkToken = null;
        return token;
    }
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
    public void Clear() { history.Clear(); _pendingSharedLinkToken = null; }
    private void Deny(string target, string path) { new CentralizedLogger().Log(new UnauthorizedAccessException($"Access denied for '{target}'."), "WPF navigation", $"target={target}; entryPath={path};"); denied("You do not have permission to access this screen."); }
}
