namespace DrawVerify.Core.Models;

public sealed class User
{
    public string UserId { get; init; } = string.Empty;
    public string Id => UserId;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; } = true;
    public bool IsMaker => string.Equals(Role, global::DrawVerify.Core.Models.Role.Maker, StringComparison.OrdinalIgnoreCase);
}
