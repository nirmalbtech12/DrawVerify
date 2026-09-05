namespace DrawVerify.Core.Models;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; }
}
