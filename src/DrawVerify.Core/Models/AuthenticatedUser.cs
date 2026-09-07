namespace DrawVerify.Core.Models;

public sealed record AuthenticatedUser(string Username, IReadOnlyList<string> Roles, long RoleVersion = 0)
{
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
