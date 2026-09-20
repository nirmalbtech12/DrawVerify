namespace DrawVerify.Core.Models;

public sealed record InternalSharedLinkUsage(long AccessCount, DateTime? LastAccessUtc, IReadOnlyList<string> AccessingUsers);
