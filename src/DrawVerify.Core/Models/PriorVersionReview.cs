using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record PriorVersionReview
{
    public Guid PackageId { get; init; }
    public Guid SubmissionId { get; init; }
    public int Revision { get; init; }
    public Guid StepDocumentVersionId { get; init; }
    public Guid DxfDocumentVersionId { get; init; }
    public Guid VerificationSessionId { get; init; }
    public string MakerUserId { get; init; } = string.Empty;
    public string MakerDisplayName { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
    public DateTime SubmittedUtc { get; init; }
    public DateTime? ApprovedUtc { get; init; }
    public WorkflowStatus Status { get; init; }
    public string? Note { get; init; }
    public string? RejectionReason { get; init; }
    public string? CorrectionResponse { get; init; }
    public IReadOnlyList<ReviewDecision> Decisions { get; init; } = Array.Empty<ReviewDecision>();
    public IReadOnlyList<ReviewComment> Comments { get; init; } = Array.Empty<ReviewComment>();
    public ReviewPackage Package { get; init; } = new();
    public bool IsReadOnly => true;
    public bool IsCurrent { get; init; }
}
