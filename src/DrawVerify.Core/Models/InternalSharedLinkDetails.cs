using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record InternalSharedLinkDetails
{
    public InternalSharedLink Link { get; init; } = new();
    public string FullUrl => Link.Url;
    public SharedLinkStatus Status { get; init; }
    public DateTime? ExpiresUtc => Link.ExpiresUtc;
    public long? AccessCount { get; init; }
    public DateTime? LastAccessUtc { get; init; }
    public IReadOnlyList<string>? AccessingUsers { get; init; }
    public bool UsageUnavailable { get; init; }
}
