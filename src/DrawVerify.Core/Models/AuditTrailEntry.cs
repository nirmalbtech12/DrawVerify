using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record AuditTrailEntry
{
    public Guid AuditId { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentRevision { get; init; }
    public string? DocumentVersion { get; init; }
    public string UserId { get; init; } = "Unavailable";
    public string UserName { get; init; } = "Unavailable";
    public string UserRole { get; init; } = "Unavailable";
    public string? UserEmail { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public DocumentAuditAction Action { get; init; }
    public string Outcome { get; init; } = "Unavailable";
    public string Details { get; init; } = "Unavailable";
    public string Identifier { get; init; } = "Unavailable";
    public string? IntegrityReference { get; init; }
}
