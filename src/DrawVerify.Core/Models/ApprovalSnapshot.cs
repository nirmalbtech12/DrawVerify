namespace DrawVerify.Core.Models;

public sealed record ApprovalSnapshot
{
    public Guid SnapshotId { get; init; } = Guid.NewGuid();
    public Guid PackageId { get; init; }
    public int Revision { get; init; }
    public string StepVersionReference { get; init; } = string.Empty;
    public string DxfVersionReference { get; init; } = string.Empty;
    public string ExtractionEvidenceJson { get; init; } = "{}";
    public string VerificationEvidenceJson { get; init; } = "{}";
    public string CheckerDecisionJson { get; init; } = "{}";
    public string StepSha256 { get; init; } = string.Empty;
    public string DxfSha256 { get; init; } = string.Empty;
    public DateTime CapturedUtc { get; init; } = DateTime.UtcNow;
    public string StepArtifactPath { get; init; } = string.Empty;
    public string DxfArtifactPath { get; init; } = string.Empty;
}
