using DrawVerify.Core.Enums;
namespace DrawVerify.Core.Models;
public sealed record DimensionMatch
{
    public Guid DimensionMatchId { get; init; } = Guid.NewGuid();
    public Guid VerificationSessionId { get; init; }
    public Guid DxfDocumentVersionId { get; init; }
    public int DxfDimensionReference { get; init; }
    public Guid StepDocumentVersionId { get; init; }
    public string? StepGeometryReference { get; init; }
    public string? GeometryType { get; init; }
    public double? ConfidenceScore { get; init; }
    public MatchConfidence? Confidence { get; init; }
    public bool RequiresManualReview { get; init; }
    public DimensionMatchStatus Status { get; init; }
    public string? ReasonCode { get; init; }
    public DimensionType? DimensionType { get; init; }
    public double? NominalValue { get; init; }
    public string? DimensionText { get; init; }
    public string? ReasonExplanation { get; init; }
}
