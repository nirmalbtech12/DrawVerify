using DrawVerify.Application.Verification;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using Xunit;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

namespace DrawVerify.Application.Tests;

public sealed class DimensionMatchingAlgorithmTests
{
    private static readonly Guid StepVersion = Guid.NewGuid();
    private static readonly Guid DxfVersion = Guid.NewGuid();

    [Fact(DisplayName = "TD-APP-001 compares linear, radial, and angular dimensions with tolerance evidence")]
    public void Matches_linear_radial_and_angular_dimensions_and_emits_missing_features()
    {
        var session = new VerificationSession { StepDocumentVersionId = StepVersion, DxfDocumentVersionId = DxfVersion };
        var dimensions = new[]
        {
            new CoreDimensionData(Guid.NewGuid(), DxfVersion, 1, DimensionType.Linear, 10),
            new CoreDimensionData(Guid.NewGuid(), DxfVersion, 2, DimensionType.Radial, 5, radialDimensionKind: RadialDimensionKind.Radius),
            new CoreDimensionData(Guid.NewGuid(), DxfVersion, 3, DimensionType.Angular, 45)
        };
        var features = new[]
        {
            new GeometryFeatureData { Reference = "Edge:1", GeometryType = "Line", Length = 10 },
            new GeometryFeatureData { Reference = "Face:2:Cylinder", GeometryType = "Hole", Radius = 5, Diameter = 10 },
            new GeometryFeatureData { Reference = "Angle:3", GeometryType = "Angle", AngleDegrees = 45 },
            new GeometryFeatureData { Reference = "Edge:unused", GeometryType = "Line", Length = 20 }
        };

        var results = new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, dimensions, features);

        Assert.Contains(results, x => x.DxfDimensionReference == 1 && x.Status == DimensionMatchStatus.Matched && x.Confidence == MatchConfidence.High);
        Assert.Contains(results, x => x.DxfDimensionReference == 2 && x.GeometryType == "Hole" && x.Status == DimensionMatchStatus.Matched);
        var angular = Assert.Single(results.Where(x => x.DxfDimensionReference == 3));
        Assert.Equal(DimensionMatchStatus.Matched, angular.Status);
        Assert.Equal(45, angular.MeasuredValue);
        Assert.Equal("degrees", angular.ToleranceUnit);
        Assert.Contains(results, x => x.Status == DimensionMatchStatus.MissingDimension && x.StepGeometryReference == "Edge:unused");
    }

    [Fact(DisplayName = "TD-APP-001 classifies ambiguous and no-candidate dimensions with stable reasons")]
    public void Ambiguous_and_no_candidate_dimensions_are_flagged_for_review()
    {
        var session = new VerificationSession { StepDocumentVersionId = StepVersion, DxfDocumentVersionId = DxfVersion };
        var dimensions = new CoreDimensionData[] { new CoreDimensionData(Guid.NewGuid(), DxfVersion, 1, DimensionType.Linear, 10), new CoreDimensionData(Guid.NewGuid(), DxfVersion, 2, DimensionType.Linear, 99) };
        var features = new[] { new GeometryFeatureData { Reference = "A", GeometryType = "Edge", Length = 10 }, new GeometryFeatureData { Reference = "B", GeometryType = "Edge", Length = 10.001 } };
        var results = new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, dimensions, features);
        var ambiguous = Assert.Single(results.Where(x => x.DxfDimensionReference == 1));
        Assert.True(ambiguous.RequiresManualReview);
        Assert.Equal("AmbiguousReference", ambiguous.ReasonCode);
        Assert.Null(ambiguous.StepGeometryReference);
        var noCandidate = Assert.Single(results.Where(x => x.DxfDimensionReference == 2));
        Assert.Equal("NoCorrespondingGeometryFound", noCandidate.ReasonCode);
        Assert.Contains("No corresponding geometry", noCandidate.ReasonExplanation);
    }

    [Fact(DisplayName = "TD-APP-001 propagates unmatched dimension metadata without geometry association")]
    public void Unmatched_results_propagate_dimension_metadata_and_have_no_geometry_reference()
    {
        var session = new VerificationSession { StepDocumentVersionId = StepVersion, DxfDocumentVersionId = DxfVersion };
        var dimension = new CoreDimensionData(Guid.NewGuid(), DxfVersion, 7, DimensionType.Radial, 5, displayText: "R5", radialDimensionKind: RadialDimensionKind.Unknown);
        var result = Assert.Single(new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, new[] { dimension }, Array.Empty<GeometryFeatureData>()));
        Assert.Equal(DimensionMatchStatus.Unmatched, result.Status);
        Assert.Equal(DimensionType.Radial, result.DimensionType);
        Assert.Equal(5, result.NominalValue);
        Assert.Equal("R5", result.DimensionText);
        Assert.Equal("DimensionTypeNotSupported", result.ReasonCode);
        Assert.True(result.RequiresManualReview);
        Assert.Null(result.StepGeometryReference);
    }

    [Fact(DisplayName = "TD-01 preserves layer, anchor, tolerances, and stable reference for DXF-only rows")]
    public void Unmatched_result_preserves_preview_and_tolerance_metadata()
    {
        var session = new VerificationSession { StepDocumentVersionId = StepVersion, DxfDocumentVersionId = DxfVersion };
        var dimension = new CoreDimensionData(Guid.NewGuid(), DxfVersion, 11, DimensionType.Linear, 25, -.2, .3, "25 ±0.2/0.3", layer: "DIMENSIONS", previewAnchor: "100,200");
        var result = Assert.Single(new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, new[] { dimension }, Array.Empty<GeometryFeatureData>()));
        Assert.Equal(DimensionMatchStatus.Unmatched, result.Status); Assert.Equal("NoCorrespondingGeometryFound", result.ReasonCode);
        Assert.Equal("DIMENSIONS", result.DxfLayer); Assert.Equal("100,200", result.PreviewAnchor); Assert.Equal((-.2, .3), (result.EffectiveLowerTolerance, result.EffectiveUpperTolerance)); Assert.Equal(11, result.DxfDimensionReference);
    }

    [Fact(DisplayName = "TD-APP-001 classifies exact tolerance boundary and explicit asymmetric bounds")]
    public void Tolerance_boundaries_and_explicit_bounds_are_persisted_in_results()
    {
        var session = new VerificationSession { StepDocumentVersionId = StepVersion, DxfDocumentVersionId = DxfVersion };
        var dimensions = new[]
        {
            new CoreDimensionData(Guid.NewGuid(), DxfVersion, 1, DimensionType.Linear, 10),
            new CoreDimensionData(Guid.NewGuid(), DxfVersion, 2, DimensionType.Linear, 10, -.2, .1),
            new CoreDimensionData(Guid.NewGuid(), DxfVersion, 3, DimensionType.Radial, 10, radialDimensionKind: RadialDimensionKind.Diameter)
        };
        var features = new[]
        {
            new GeometryFeatureData { Reference = "boundary", GeometryType = "Edge", Length = 10.1 },
            new GeometryFeatureData { Reference = "explicit", GeometryType = "Edge", Length = 9.8 },
            new GeometryFeatureData { Reference = "diameter", GeometryType = "Hole", Diameter = 10.001 }
        };
        var results = new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, dimensions, features);
        Assert.All(results.Where(x => x.DxfDimensionReference > 0), result => Assert.Equal(DimensionMatchStatus.Matched, result.Status));
        Assert.Equal(ToleranceSource.Default, results.Single(x => x.DxfDimensionReference == 1).ToleranceSource);
        Assert.Equal(ToleranceSource.Explicit, results.Single(x => x.DxfDimensionReference == 2).ToleranceSource);
        Assert.Equal("mm", results.Single(x => x.DxfDimensionReference == 3).ToleranceUnit);
    }

    [Fact(DisplayName = "TD-APP-001 marks values outside tolerance as mismatched")]
    public void Values_outside_tolerance_are_mismatched_with_reason()
    {
        var session = new VerificationSession { StepDocumentVersionId = StepVersion, DxfDocumentVersionId = DxfVersion };
        var dimension = new CoreDimensionData(Guid.NewGuid(), DxfVersion, 1, DimensionType.Linear, 10);
        var result = Assert.Single(new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, new[] { dimension }, new[] { new GeometryFeatureData { Reference = "edge", GeometryType = "Edge", Length = 10.101 } }));
        Assert.Equal(DimensionMatchStatus.Mismatched, result.Status);
        Assert.Equal("OutsideTolerance", result.ReasonCode);
        Assert.Equal(10.101, result.MeasuredValue);
        Assert.True(result.RequiresManualReview);
    }
}
