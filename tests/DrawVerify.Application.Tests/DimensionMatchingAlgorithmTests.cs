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

    [Fact]
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
            new GeometryFeatureData { Reference = "Edge:unused", GeometryType = "Line", Length = 20 }
        };

        var results = new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, dimensions, features);

        Assert.Contains(results, x => x.DxfDimensionReference == 1 && x.Status == DimensionMatchStatus.Matched && x.Confidence == MatchConfidence.High);
        Assert.Contains(results, x => x.DxfDimensionReference == 2 && x.GeometryType == "Hole" && x.Status == DimensionMatchStatus.Matched);
        var angular = Assert.Single(results.Where(x => x.DxfDimensionReference == 3));
        Assert.Equal(DimensionMatchStatus.Unmatched, angular.Status);
        Assert.Equal("DimensionTypeNotSupported", angular.ReasonCode);
        Assert.Contains("not supported", angular.ReasonExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(results, x => x.Status == DimensionMatchStatus.MissingDimension && x.StepGeometryReference == "Edge:unused");
    }

    [Fact]
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

    [Fact]
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
}
