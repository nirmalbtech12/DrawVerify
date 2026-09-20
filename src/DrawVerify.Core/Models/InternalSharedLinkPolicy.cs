using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record InternalSharedLinkPolicy
{
    public bool AllowDownload { get; init; }
    public bool AllowTracking { get; init; } = true;
    public bool AllowMultipleLinks { get; init; }
    public bool AllowExpiration { get; init; } = true;
    public IReadOnlySet<string> AllowedRoles { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Approver", "Viewer" };
    public IReadOnlySet<string> AllowedGroups { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string InternalDomain { get; init; } = string.Empty;
    public bool IsAccessLevelAllowed(SharedLinkAccessLevel level) => level != SharedLinkAccessLevel.ViewAndDownload || AllowDownload;
}
