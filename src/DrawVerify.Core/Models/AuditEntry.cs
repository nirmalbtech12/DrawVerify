using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class AuditEntry
{
<<<<<<< HEAD
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public string Action { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
=======
    public Guid AuditId { get; init; } = Guid.NewGuid();
    public Guid? DocumentId { get; init; }
    public DocumentAuditAction Action { get; init; }
    public string UserId { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public DateTime Timestamp => TimestampUtc;
>>>>>>> 642060c (KAN-19: implementation)
    public string DetailsJson { get; init; } = "{}";
}
