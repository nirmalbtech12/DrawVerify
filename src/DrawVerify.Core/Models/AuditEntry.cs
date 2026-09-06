using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class AuditEntry
{
    public Guid AuditId { get; init; } = Guid.NewGuid();
    public Guid? DocumentId { get; init; }
    public DocumentAuditAction Action { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public DateTime Timestamp => TimestampUtc;
    public string DetailsJson { get; init; } = "{}";
}
