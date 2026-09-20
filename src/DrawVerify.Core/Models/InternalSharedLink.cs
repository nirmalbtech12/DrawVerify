using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record InternalSharedLink
{
    public Guid LinkId { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public string Token { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public SharedLinkAccessLevel AccessLevel { get; init; }
    public bool IsActive { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public bool TrackingEnabled { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime? RevokedUtc { get; init; }
    public SharedLinkStatus Status(DateTime utcNow) => !IsActive || RevokedUtc.HasValue ? SharedLinkStatus.Inactive : ExpiresUtc <= utcNow ? SharedLinkStatus.Expired : SharedLinkStatus.Active;
    public string TokenHash => Token;
}
