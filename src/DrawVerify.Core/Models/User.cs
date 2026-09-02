namespace DrawVerify.Core.Models;

public sealed class User
{
<<<<<<< HEAD
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; }
=======
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsMaker => string.Equals(Role, global::DrawVerify.Core.Models.Role.Maker, StringComparison.OrdinalIgnoreCase);
>>>>>>> 642060c (KAN-19: implementation)
}
