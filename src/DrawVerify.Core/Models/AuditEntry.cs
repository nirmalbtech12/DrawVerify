namespace DrawVerify.Core.Models;

public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public string Action { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string DetailsJson { get; init; } = "{}";
}
