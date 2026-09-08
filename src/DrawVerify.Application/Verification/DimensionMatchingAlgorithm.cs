using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

namespace DrawVerify.Application.Verification;

public sealed class DimensionMatchingAlgorithm
{
    private readonly DimensionMatchingConfiguration _configuration;
    public DimensionMatchingAlgorithm(DimensionMatchingConfiguration configuration) { _configuration = configuration; _configuration.Validate(); }

    public IReadOnlyList<DimensionMatch> Match(VerificationSession session, IReadOnlyList<CoreDimensionData> dimensions, IReadOnlyList<GeometryFeatureData> features, CancellationToken cancellationToken = default)
    {
        var results = new List<DimensionMatch>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dimension in dimensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Unknown)
            {
                results.Add(Unmatched(session, dimension, "DimensionTypeNotSupported", "Dimension type not supported for matching (unknown radial subtype)."));
                continue;
            }
            var candidates = features.Where(feature => IsCandidate(dimension, feature)).Select(feature => (feature, score: Score(dimension, feature))).OrderByDescending(candidate => candidate.score).ToList();
            if (dimension.DimensionType == DimensionType.Angular)
            {
                results.Add(Unmatched(session, dimension, "DimensionTypeNotSupported", "Dimension type not supported for matching (angular dimensions)."));
                continue;
            }
            if (candidates.Count == 0) { results.Add(Unmatched(session, dimension, "NoCorrespondingGeometryFound", "No corresponding geometry found in STEP model.")); continue; }
            var selected = candidates[0];
            if (candidates.Count > 1 && Math.Abs(selected.score - candidates[1].score) < 0.05)
            {
                results.Add(Unmatched(session, dimension, "AmbiguousReference", "Ambiguous reference – multiple possible matches."));
                continue;
            }
            var confidence = selected.score >= _configuration.HighConfidenceThreshold ? MatchConfidence.High : selected.score >= _configuration.MediumConfidenceThreshold ? MatchConfidence.Medium : MatchConfidence.Low;
            var review = confidence != MatchConfidence.High || candidates.Count > 1 && Math.Abs(selected.score - candidates[1].score) < 0.05;
            used.Add(selected.feature.Reference);
            results.Add(new DimensionMatch { VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = dimension.DocumentVersionId, DxfDimensionReference = dimension.SourceEntityOrdinal, StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty, StepGeometryReference = selected.feature.Reference, GeometryType = selected.feature.GeometryType, ConfidenceScore = selected.score, Confidence = confidence, RequiresManualReview = review, Status = DimensionMatchStatus.Matched, DimensionType = dimension.DimensionType, NominalValue = dimension.NominalValue, DimensionText = dimension.DimensionText });
        }
        foreach (var feature in features.Where(feature => !used.Contains(feature.Reference)))
            results.Add(new DimensionMatch { VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = session.DxfDocumentVersionId ?? Guid.Empty, DxfDimensionReference = -1, StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty, StepGeometryReference = feature.Reference, GeometryType = feature.GeometryType, Status = DimensionMatchStatus.MissingDimension, RequiresManualReview = true, ReasonCode = "NoCorrespondingDimension" });
        return results;
    }

    private bool IsCandidate(CoreDimensionData dimension, GeometryFeatureData feature)
    {
        var value = dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Radius ? feature.Radius : dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Diameter ? feature.Diameter : feature.Length ?? feature.Width ?? feature.Height ?? feature.Distance;
        return value is not null && double.IsFinite(value.Value) && Within(value.Value, dimension.NominalValue);
    }
    private double Score(CoreDimensionData dimension, GeometryFeatureData feature)
    {
        var value = dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Radius ? feature.Radius : dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Diameter ? feature.Diameter : feature.Length ?? feature.Width ?? feature.Height ?? feature.Distance;
        if (value is null) return 0;
        var difference = Math.Abs(value.Value - dimension.NominalValue);
        return Math.Max(0, 1 - difference / Math.Max(Math.Abs(dimension.NominalValue), 1));
    }
    private bool Within(double actual, double expected) => Math.Abs(actual - expected) <= _configuration.AbsoluteTolerance + Math.Abs(expected) * _configuration.RelativeTolerance;
    private static DimensionMatch Unmatched(VerificationSession session, CoreDimensionData dimension, string reason, string explanation) => new() { VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = dimension.DocumentVersionId, DxfDimensionReference = dimension.SourceEntityOrdinal, StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty, Status = DimensionMatchStatus.Unmatched, RequiresManualReview = true, ReasonCode = reason, ReasonExplanation = explanation, DimensionType = dimension.DimensionType, NominalValue = dimension.NominalValue, DimensionText = dimension.DimensionText };
}
