namespace DrawVerify.Core.Models;

public sealed record AuthenticatedUser(string Username, IReadOnlyList<string> Roles)
{
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
