using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record ReviewSubmission
{
    public Guid SubmissionId { get; init; } = Guid.NewGuid();
    public Guid PackageId { get; init; }
    public Guid DxfDocumentId { get; init; }
    public Guid StepDocumentVersionId { get; init; }
    public Guid DxfDocumentVersionId { get; init; }
    public Guid VerificationSessionId { get; init; }
    public string ExtractionEvidenceJson { get; init; } = "{}";
    public string VerificationEvidenceJson { get; init; } = "{}";
    public int Revision { get; init; } = 1;
    public string MakerUserId { get; init; } = string.Empty;
    public string MakerDisplayName { get; init; } = string.Empty;
    public DateTime SubmittedUtc { get; init; } = DateTime.UtcNow;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string? Note { get; init; }
    public WorkflowStatus Status { get; init; } = WorkflowStatus.SubmittedForReview;
    public bool MakerAuthorized { get; init; } = true;
    public string? AssignedCheckerUserId { get; init; }
    public IReadOnlyList<string> AssignedCheckerUserIds { get; init; } = Array.Empty<string>();
    public bool AvailableToCheckers { get; init; } = true;
    public string? AssignedApproverUserId { get; init; }
    public IReadOnlyList<string> AssignedApproverUserIds { get; init; } = Array.Empty<string>();
    public bool AvailableToApprovers { get; init; } = true;
    public DocumentFingerprint? Fingerprint { get; init; }
    public SignatureData? MakerSignature { get; init; }
    public SignatureData? CheckerSignature { get; init; }
    public SignatureData? ApproverSignature { get; init; }
    public InternalApprovalStamp? InternalApprovalStamp { get; init; }
    public TemplateMappingSnapshot? TemplateMapping { get; init; }
    public IReadOnlyList<SignaturePlaceholderAssignment> PlaceholderAssignments { get; init; } = Array.Empty<SignaturePlaceholderAssignment>();
    public IReadOnlyList<SignaturePlaceholderAssignment> EligiblePlaceholders(string userId, IEnumerable<string> roles) => PlaceholderAssignments.Where(x => !x.IsSigned && x.IsEligible(userId, roles)).ToArray();
}

public sealed record SubmissionReadiness(bool IsReady, IReadOnlyList<string> Failures)
{
    public static SubmissionReadiness Ready { get; } = new(true, Array.Empty<string>());
}

public sealed record SubmissionRequest(Guid PackageId, string? Note = null)
{
    public Guid DxfDocumentId { get; init; }
    public Guid StepDocumentVersionId { get; init; }
    public Guid DxfDocumentVersionId { get; init; }
    public Guid VerificationSessionId { get; init; }
    public string ExtractionEvidenceJson { get; init; } = "{}";
    public string VerificationEvidenceJson { get; init; } = "{}";
    public bool DxfAvailable { get; init; } = true;
    public bool ExtractionCompleted { get; init; } = true;
    public bool VerificationCompleted { get; init; } = true;
    public bool RequiredMetadataPresent { get; init; } = true;
    public string? ProcessingError { get; init; }
    public string? VerificationError { get; init; }
    public IProgress<long>? FingerprintProgress { get; init; }
    public SignatureData? MakerSignature { get; init; }
    public TemplateMappingSnapshot? TemplateMapping { get; init; }
    public IReadOnlyList<SignaturePlaceholderAssignment> PlaceholderAssignments { get; init; } = Array.Empty<SignaturePlaceholderAssignment>();
    public string? AssignedCheckerUserId { get; init; }
    public IReadOnlyList<string> AssignedCheckerUserIds { get; init; } = Array.Empty<string>();
    public string? AssignedApproverUserId { get; init; }
    public IReadOnlyList<string> AssignedApproverUserIds { get; init; } = Array.Empty<string>();
}
public sealed record SubmissionResult(bool Succeeded, string Message, WorkflowState? State = null, ReviewSubmission? Submission = null, IReadOnlyList<string>? Failures = null)
{
    public IReadOnlyList<string> Errors => Failures ?? Array.Empty<string>();
}
public sealed record WorkflowQueueItem(Guid PackageId, int Revision, string MakerDisplayName, DateTime SubmittedUtc, WorkflowStatus Status, string VerificationSummary)
{
    public string? AssignedCheckerUserId { get; init; }
    public bool AvailableToCheckers { get; init; } = true;
    public string? AssignedApproverUserId { get; init; }
    public bool AvailableToApprovers { get; init; } = true;
}
