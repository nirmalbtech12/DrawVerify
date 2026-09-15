using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class AuditEntry
{
    public Guid AuditId { get; init; } = Guid.NewGuid();
    public Guid? DocumentId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string? ActorRole { get; init; }
    public string? DocumentRevision { get; init; }
    public string? DocumentVersion { get; init; }
    public string EventMetadataJson { get; init; } = "{}";
    public string? PreviousWorkflowStatus { get; init; }
    public string? NewWorkflowStatus { get; init; }
    public Guid? PredecessorAuditId { get; init; }
    public string? IntegrityHash { get; init; }
    public DocumentAuditAction Action { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public DateTime Timestamp => TimestampUtc;
    public DateTimeOffset TimestampOffset { get; init; }
    public string DetailsJson { get; init; } = "{}";

    public void Validate()
    {
        if (!DocumentId.HasValue || DocumentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(DocumentId));
        if (string.IsNullOrWhiteSpace(UserId)) throw new ArgumentException("Audit actor is required.", nameof(UserId));
        if (!Enum.IsDefined(Action)) throw new ArgumentException("A supported audit action is required.", nameof(Action));
        if (TimestampUtc == default && TimestampOffset == default) throw new ArgumentException("Audit timestamp is required.", nameof(TimestampUtc));
    }
}
