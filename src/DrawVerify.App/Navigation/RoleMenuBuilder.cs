using System.Windows;
using System.Windows.Controls;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.App.Navigation;

public sealed class RoleMenuBuilder(ISessionAuthenticationService session, RoleAuthorization authorization)
{
    private readonly ScreenAccessGuard _guard = new(session, authorization);
    public IReadOnlyList<MenuItem> Build(Action<string> navigate, Func<Task> logout, Action<string> denied)
    {
        return ScreenRegistry.Menus.Select(menu => BuildItem(menu, navigate, logout, denied)).Where(x => x is not null).Cast<MenuItem>().ToArray();
    }
    private MenuItem? BuildItem(MenuDefinition definition, Action<string> navigate, Func<Task> logout, Action<string> denied)
    {
        var allowed = definition.Permission is null || authorization.IsAllowed(session.CurrentUser, definition.Permission.Value);
        if (definition.Target == "Admin" && definition.Children is not null)
            allowed = definition.Children.Any(child => child.Permission is null || authorization.IsAllowed(session.CurrentUser, child.Permission.Value));
        if (definition.RequiredRole is not null) allowed &= session.CurrentUser?.Roles.Any(x => string.Equals(x, definition.RequiredRole, StringComparison.OrdinalIgnoreCase)) == true;
        if (definition.Children is null || definition.Children.Count == 0)
        {
            if (!allowed && definition.Target != "Logout") return null;
            var leaf = new MenuItem { Header = definition.Header, IsEnabled = allowed };
            leaf.Click += async (_, _) => { if (definition.Target == "Logout") { if (allowed) await logout(); else denied(definition.Header); return; } if (!allowed || !_guard.CanAccess(definition.Target)) { denied(definition.Header); return; } navigate(definition.Target); };
            return leaf;
        }
        if (!allowed && definition.Target != "File") return null;
        var item = new MenuItem { Header = definition.Header, IsEnabled = allowed };
        foreach (var child in definition.Children.Select(x => BuildItem(x, navigate, logout, denied)).Where(x => x is not null)) item.Items.Add(child);
        return item;
    }
}
