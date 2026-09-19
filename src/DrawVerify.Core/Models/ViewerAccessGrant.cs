namespace DrawVerify.Core.Models;

public sealed record ViewerAccessGrant
{
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public ViewerAccessTarget Target { get; init; } = new(ViewerAccessTargetKind.User, string.Empty, string.Empty);
    public DateTime GrantedUtc { get; init; }
    public string GrantedByUserId { get; init; } = string.Empty;
    public string GrantedByName { get; init; } = string.Empty;
    public DateTime? ExpiresUtc { get; init; }
    public bool IsRevoked { get; init; }
    public bool IsExpired(DateTime utcNow) => ExpiresUtc.HasValue && ExpiresUtc.Value <= utcNow;
    public bool IsEffective(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);
    public IReadOnlyList<ViewerAccessTarget> Members { get; init; } = Array.Empty<ViewerAccessTarget>();
}
