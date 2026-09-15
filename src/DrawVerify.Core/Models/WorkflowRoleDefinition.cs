using DrawVerify.Core.Authorization;
namespace DrawVerify.Core.Models;
public sealed record WorkflowRoleDefinition
{
    public string DisplayName { get; init; } = string.Empty;
    public string SystemRole { get; init; } = SystemRoles.Viewer;
    public IReadOnlyList<RolePermission> Permissions { get; init; } = Array.Empty<RolePermission>();
}
