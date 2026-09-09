namespace DrawVerify.Application.Verification;
using DrawVerify.Core.Models;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

/// <summary>
/// Detects required dimensions that are missing from the drawing document.
/// </summary>
public sealed class MissingDimensionDetector
{
    public IReadOnlyList<CoreDimensionData> Detect(IReadOnlyList<CoreDimensionData> dimensions, IEnumerable<int> matchedDimensionReferences)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(matchedDimensionReferences);
        var matched = matchedDimensionReferences.ToHashSet();
        return dimensions.Where(dimension => !matched.Contains(dimension.SourceEntityOrdinal)).ToArray();
    }

    public IReadOnlyList<DimensionMatch> Detect(VerificationSession session, IReadOnlyList<CoreDimensionData> dimensions, IEnumerable<int> matchedDimensionReferences)
    {
        return Detect(dimensions, matchedDimensionReferences).Select(dimension => new DimensionMatch
        {
            VerificationSessionId = session.VerificationSessionId,
            DxfDocumentVersionId = dimension.DocumentVersionId,
            DxfDimensionReference = dimension.SourceEntityOrdinal,
            StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty,
            Status = DrawVerify.Core.Enums.DimensionMatchStatus.Unmatched,
            RequiresManualReview = true,
            ReasonCode = "NoCorrespondingGeometryFound",
            ReasonExplanation = "No corresponding geometry found in STEP model.",
            DimensionType = dimension.DimensionType,
            NominalValue = dimension.NominalValue,
            DimensionText = dimension.DimensionText,
            EffectiveLowerTolerance = dimension.LowerTolerance,
            EffectiveUpperTolerance = dimension.UpperTolerance,
            ToleranceUnit = dimension.DimensionType == DrawVerify.Core.Enums.DimensionType.Angular ? "°" : "mm",
            DxfLayer = dimension.Layer,
            PreviewAnchor = dimension.PreviewAnchor
        }).ToArray();
    }
}
