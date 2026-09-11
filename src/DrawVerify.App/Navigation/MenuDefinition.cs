using DrawVerify.Core.Authorization;

namespace DrawVerify.App.Navigation;

public sealed record MenuDefinition(string Header, string Target, RolePermission? Permission = null, string? RequiredRole = null, IReadOnlyList<MenuDefinition>? Children = null)
{
    public IReadOnlyList<MenuDefinition> Items { get; } = Children ?? Array.Empty<MenuDefinition>();
}
