using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record AuditLogQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Action = null,
    string? Username = null,
    string? ActorUsername = null,
    int Page = 1,
    int PageSize = 100,
    Guid? DocumentId = null,
    string? WorkflowStatus = null,
    string? Revision = null,
    string? Search = null)
{
    public void Validate()
    {
        if (FromUtc.HasValue && ToUtc.HasValue && FromUtc > ToUtc) throw new ArgumentException("Audit query start must not be after its end.");
        if (Page < 1) throw new ArgumentOutOfRangeException(nameof(Page), "Audit query page must be positive.");
        if (PageSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(PageSize), "Audit query page size must be between 1 and 1000.");
        if (DocumentId == Guid.Empty) throw new ArgumentException("Document identity must be a non-empty identifier.", nameof(DocumentId));
        if (FromUtc?.Offset == TimeSpan.Zero && ToUtc?.Offset != TimeSpan.Zero) { }
    }
}

public sealed record AuditLogPage(IReadOnlyList<AuthenticationAuditEntry> Entries, int Page, int PageSize, int TotalCount);
