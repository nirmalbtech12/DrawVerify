namespace DrawVerify.Core.Models;

public sealed record AuthenticatedUser(string Username, IReadOnlyList<string> Roles, long RoleVersion = 0)
{
    public string UserId { get; init; } = Username;
    public string DisplayName { get; init; } = Username;
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
