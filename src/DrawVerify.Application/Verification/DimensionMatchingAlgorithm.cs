using System.Globalization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

namespace DrawVerify.Application.Verification;

public sealed class DimensionMatchingAlgorithm
{
    private readonly DimensionMatchingConfiguration _configuration;
    private readonly MissingDimensionDetector _missingDimensionDetector = new();
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
            var candidates = features.Where(feature => !used.Contains(feature.Reference) && IsCandidate(dimension, feature)).Select(feature =>
            {
                var value = Measure(dimension, feature)!.Value;
                var tolerance = ToleranceComparer.Compare(dimension, value, _configuration, session.ToleranceThresholdMm);
                return (feature, score: Score(dimension, feature), withinTolerance: tolerance.IsWithinTolerance);
            }).OrderByDescending(candidate => candidate.withinTolerance).ThenByDescending(candidate => candidate.score).ToList();
            if (candidates.Count == 0) { results.AddRange(_missingDimensionDetector.Detect(session, new[] { dimension }, Array.Empty<int>())); continue; }
            var selected = candidates[0];
            if (candidates.Count > 1 && selected.withinTolerance == candidates[1].withinTolerance && Math.Abs(selected.score - candidates[1].score) < 0.05)
            {
                results.Add(Unmatched(session, dimension, "AmbiguousReference", "Ambiguous reference – multiple possible matches."));
                continue;
            }
            var confidence = selected.score >= _configuration.HighConfidenceThreshold ? MatchConfidence.High : selected.score >= _configuration.MediumConfidenceThreshold ? MatchConfidence.Medium : MatchConfidence.Low;
            var review = confidence != MatchConfidence.High || candidates.Count > 1 && Math.Abs(selected.score - candidates[1].score) < 0.05;
            used.Add(selected.feature.Reference);
            var measured = Measure(dimension, selected.feature);
            if (measured is null) { results.Add(Unmatched(session, dimension, "MeasuredValueUnavailable", "The selected STEP geometry did not provide a finite measurement.")); continue; }
            var tolerance = ToleranceComparer.Compare(dimension, measured.Value, _configuration, session.ToleranceThresholdMm);
            var status = tolerance.IsWithinTolerance ? DimensionMatchStatus.Matched : DimensionMatchStatus.Mismatched;
            var reason = tolerance.IsWithinTolerance ? null : "OutsideTolerance";
            var explanation = tolerance.IsWithinTolerance ? null : $"Measured value {FormatValue(measured.Value)} is outside [{FormatValue(dimension.NominalValue + tolerance.Lower)}, {FormatValue(dimension.NominalValue + tolerance.Upper)}] {tolerance.Unit}.";
            results.Add(new DimensionMatch { VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = dimension.DocumentVersionId, DxfDimensionReference = dimension.SourceEntityOrdinal, StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty, StepGeometryReference = selected.feature.Reference, GeometryType = selected.feature.GeometryType, ConfidenceScore = selected.score, Confidence = confidence, RequiresManualReview = review || !tolerance.IsWithinTolerance, Status = status, ReasonCode = reason, ReasonExplanation = explanation, DimensionType = dimension.DimensionType, NominalValue = dimension.NominalValue, DimensionText = dimension.DimensionText, MeasuredValue = measured, EffectiveLowerTolerance = tolerance.Lower, EffectiveUpperTolerance = tolerance.Upper, ToleranceUnit = tolerance.Unit, ToleranceSource = tolerance.Source, DxfLayer = dimension.Layer, PreviewAnchor = dimension.PreviewAnchor, BlockName = dimension.BlockName, ViewName = dimension.ViewName, LocationX = dimension.LocationX, LocationY = dimension.LocationY });
        }
        foreach (var feature in features.Where(feature => !used.Contains(feature.Reference)))
            results.Add(new DimensionMatch { VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = session.DxfDocumentVersionId ?? Guid.Empty, DxfDimensionReference = -1, StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty, StepGeometryReference = feature.Reference, GeometryType = feature.GeometryType, StepFeatureValue = Finite(feature.FeatureValue ?? feature.Length ?? feature.Width ?? feature.Height ?? feature.Distance ?? feature.Radius ?? feature.Diameter ?? feature.AngleDegrees), StepLocationX = Finite(feature.LocationX), StepLocationY = Finite(feature.LocationY), StepLocationZ = Finite(feature.LocationZ), Status = DimensionMatchStatus.ExtraDimension, RequiresManualReview = true, ReasonCode = "NoCorrespondingDimension" });
        return results;
    }

    private bool IsCandidate(CoreDimensionData dimension, GeometryFeatureData feature)
    {
        var value = Measure(dimension, feature);
        return value is not null && double.IsFinite(value.Value) && Score(dimension, feature) >= 0.5;
    }
    private double Score(CoreDimensionData dimension, GeometryFeatureData feature)
    {
        var value = Measure(dimension, feature);
        if (value is null) return 0;
        var difference = Math.Abs(value.Value - dimension.NominalValue);
        return Math.Max(0, 1 - difference / Math.Max(Math.Abs(dimension.NominalValue), 1));
    }
    private static double? Measure(CoreDimensionData dimension, GeometryFeatureData feature) => dimension.DimensionType == DimensionType.Angular ? feature.AngleDegrees : dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Radius ? feature.Radius : dimension.DimensionType == DimensionType.Radial && dimension.RadialKind == RadialDimensionKind.Diameter ? feature.Diameter : feature.Length ?? feature.Width ?? feature.Height ?? feature.Distance;
    private static string FormatValue(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    private static double? Finite(double? value) => value is double number && double.IsFinite(number) ? number : null;
    private static DimensionMatch Unmatched(VerificationSession session, CoreDimensionData dimension, string reason, string explanation) => new() { VerificationSessionId = session.VerificationSessionId, DxfDocumentVersionId = dimension.DocumentVersionId, DxfDimensionReference = dimension.SourceEntityOrdinal, StepDocumentVersionId = session.StepDocumentVersionId ?? Guid.Empty, Status = DimensionMatchStatus.Unmatched, RequiresManualReview = true, ReasonCode = reason, ReasonExplanation = explanation, DimensionType = dimension.DimensionType, NominalValue = dimension.NominalValue, DimensionText = dimension.DimensionText, EffectiveLowerTolerance = dimension.LowerTolerance, EffectiveUpperTolerance = dimension.UpperTolerance, ToleranceUnit = dimension.DimensionType == DimensionType.Angular ? "°" : "mm", DxfLayer = dimension.Layer, PreviewAnchor = dimension.PreviewAnchor, BlockName = dimension.BlockName, ViewName = dimension.ViewName, LocationX = dimension.LocationX, LocationY = dimension.LocationY };
}
