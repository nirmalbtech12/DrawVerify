namespace DrawVerify.Application.Tests;
using DrawVerify.Application.Verification;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

/// <summary>
/// Tests for MissingDimensionDetector (required-but-absent dimensions).
/// </summary>
public class MissingDimensionDetectorTests
{
    [Fact(DisplayName = "TD-01 classifies every DXF dimension absent from STEP with stable metadata")]
    public void Detect_returns_only_unmatched_dxf_dimensions_and_preserves_metadata()
    {
        var version = Guid.NewGuid();
        var dimensions = new[]
        {
            new DimensionData(Guid.NewGuid(), version, 1, DimensionType.Linear, 10, -.1, .1, "10 ±0.1", layer: "DIM", previewAnchor: "1,2"),
            new DimensionData(Guid.NewGuid(), version, 2, DimensionType.Linear, 20, displayText: "20", layer: "ANNOTATION", previewAnchor: "3,4"),
            new DimensionData(Guid.NewGuid(), version, 3, DimensionType.Angular, 45, displayText: "45°")
        };
        var missing = new MissingDimensionDetector().Detect(dimensions, new[] { 1 });
        Assert.Equal(new[] { 2, 3 }, missing.Select(dimension => dimension.SourceEntityOrdinal));
        Assert.Equal("ANNOTATION", missing[0].Layer); Assert.Equal("3,4", missing[0].PreviewAnchor); Assert.Equal("45°", missing[1].DimensionText);
    }

    [Fact(DisplayName = "TD-01 creates directional Unmatched results with explicit reason")]
    public void Detect_creates_unmatched_results_with_explicit_reason()
    {
        var session = new VerificationSession { VerificationSessionId = Guid.NewGuid(), StepDocumentVersionId = Guid.NewGuid() };
        var dimension = new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 7, DimensionType.Linear, 12, displayText: "12 mm");
        var result = Assert.Single(new MissingDimensionDetector().Detect(session, new[] { dimension }, Array.Empty<int>()));
        Assert.Equal(DimensionMatchStatus.Unmatched, result.Status); Assert.Equal("NoCorrespondingGeometryFound", result.ReasonCode);
        Assert.Equal(7, result.DxfDimensionReference); Assert.Equal(12, result.NominalValue); Assert.Equal("12 mm", result.DimensionText); Assert.Null(result.StepGeometryReference);
    }
}
