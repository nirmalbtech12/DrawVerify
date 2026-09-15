using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record DocumentAuditRecord
{
    public Guid AuditId { get; init; }
    public Guid DocumentId { get; init; }
    public string? DocumentRevision { get; init; }
    public string? DocumentVersion { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string? ActorRole { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public DocumentAuditAction Action { get; init; }
    public string DetailsJson { get; init; } = "{}";
    public string EventMetadataJson { get; init; } = "{}";
    public string? PreviousWorkflowStatus { get; init; }
    public string? NewWorkflowStatus { get; init; }
    public Guid? PredecessorAuditId { get; init; }
    public string? IntegrityHash { get; init; }
}
