using DrawVerify.Core.Enums;
using System.Text.Json;

namespace DrawVerify.Core.Models;

public sealed record ReviewPackage
{
    public Guid PackageId { get; init; }
    public Guid StepDocumentId { get; init; }
    public Guid DxfDocumentId { get; init; }
    public Guid VerificationSessionId { get; init; }
    public int Revision { get; init; }
    public WorkflowStatus Status { get; init; }
    public string MakerUserId { get; init; } = string.Empty;
    public string MakerDisplayName { get; init; } = string.Empty;
    public DateTime SubmittedUtc { get; init; }
    public string? Note { get; init; }
    public Guid StepDocumentVersionId { get; init; }
    public Guid DxfDocumentVersionId { get; init; }
    public string ExtractionEvidenceJson { get; init; } = "{}";
    public string VerificationEvidenceJson { get; init; } = "{}";
    public string SnapshotReference { get; init; } = string.Empty;
    public ReviewSubmission Submission { get; init; } = new();
    public ReviewDecision? PriorDecision { get; init; }
    public IReadOnlyList<ReviewComment> Comments { get; init; } = Array.Empty<ReviewComment>();
    public IReadOnlyList<ReviewFinding> VerificationFindings { get; init; } = Array.Empty<ReviewFinding>();
    public static ReviewPackage FromSubmission(ReviewSubmission submission, ReviewDecision? priorDecision = null, IReadOnlyList<ReviewComment>? comments = null)
    {
        var findings = new List<ReviewFinding>();
        using var json = JsonDocument.Parse(submission.VerificationEvidenceJson);
        if (json.RootElement.TryGetProperty("findings", out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var item in array.EnumerateArray())
            {
                var identifier = item.TryGetProperty("identifier", out var id) ? id.GetString() : null;
                if (!string.IsNullOrWhiteSpace(identifier)) findings.Add(new ReviewFinding(identifier, item.TryGetProperty("category", out var category) ? category.GetString() ?? "Verification" : "Verification", item.TryGetProperty("description", out var description) ? description.GetString() : null, item.TryGetProperty("highlightTarget", out var highlight) ? highlight.GetString() : identifier));
            }
        return new()
        {
        PackageId = submission.PackageId, Revision = submission.Revision, Status = submission.Status,
        DxfDocumentId = submission.DxfDocumentId, VerificationSessionId = submission.VerificationSessionId,
        MakerUserId = submission.MakerUserId, MakerDisplayName = submission.MakerDisplayName, SubmittedUtc = submission.SubmittedUtc,
        Note = submission.Note, StepDocumentVersionId = submission.StepDocumentVersionId, DxfDocumentVersionId = submission.DxfDocumentVersionId,
        ExtractionEvidenceJson = submission.ExtractionEvidenceJson, VerificationEvidenceJson = submission.VerificationEvidenceJson,
        SnapshotReference = submission.SubmissionId.ToString(), Submission = submission, PriorDecision = priorDecision,
        Comments = comments ?? Array.Empty<ReviewComment>(), VerificationFindings = findings
        };
    }
}
