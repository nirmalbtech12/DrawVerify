using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record InternalSharedLinkConfiguration
{
    public SharedLinkAccessLevel AccessLevel { get; init; } = SharedLinkAccessLevel.ViewOnly;
    public bool IsActive { get; init; } = true;
    public DateTime? ExpiresUtc { get; init; }
    public bool TrackingEnabled { get; init; } = true;
    public bool AllowMultipleLinks { get; init; }
    public bool ShowAccessingUsers { get; init; }
    public void Validate(DateTime? nowUtc = null)
    {
        if (ExpiresUtc.HasValue && ExpiresUtc.Value.Kind != DateTimeKind.Utc) throw new ArgumentException("The expiration must be a UTC date and time.", nameof(ExpiresUtc));
        if (ExpiresUtc.HasValue && ExpiresUtc.Value <= (nowUtc ?? DateTime.UtcNow)) throw new ArgumentException("The expiration must be in the future.", nameof(ExpiresUtc));
    }
}
