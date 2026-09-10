using DrawVerify.Geometry.Models;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.Geometry.Tests;

public sealed class MeasurementExtractionResultTests
{
    [Fact(DisplayName = "TD-GEO-001 maps complete solid result")]
    public void Solid_result_retains_geometry_and_reliable_features()
    {
        var geometry = new GeometryData { Volume = 12.5, BoundingBox = new BoundingBox(0, 1, 2, 4, 6, 8), FaceCount = 6, EdgeCount = 12, VertexCount = 8, Unit = "mm" };
        var result = new MeasurementExtractionResult(true, geometry,
            new[] { new FeatureData { FeatureType = "Hole", NominalDimensions = new Dictionary<string, double> { ["Diameter"] = 2 } } });

        Assert.True(result.Succeeded);
        Assert.Equal(12.5, result.Geometry!.Volume);
        Assert.Equal(4, result.Geometry.BoundingBox!.Length);
        Assert.Equal(8, result.Geometry.VertexCount);
        Assert.Equal("Hole", Assert.Single(result.Features).FeatureType);
    }

    [Fact(DisplayName = "TD-GEO-001 maps partial failures without speculative features")]
    public void Failure_result_retains_partial_data_and_safe_category()
    {
        var partial = new GeometryData { BoundingBox = new BoundingBox(0, 0, 0, 1, 2, 3), FaceCount = 1, EdgeCount = 2, VertexCount = 3 };
        var result = MeasurementExtractionResult.Failure("NonSolid", "Model does not contain a valid solid; volume cannot be computed.", partial);

        Assert.False(result.Succeeded);
        Assert.Null(result.Geometry!.Volume);
        Assert.Equal("NonSolid", result.ErrorCategory);
        Assert.Empty(result.Features);
        Assert.Contains("valid solid", result.ErrorMessage);
    }

    [Fact(DisplayName = "TD-05 preserves managed GD&T source references without native types")]
    public void Gdt_annotation_contract_preserves_step_source_reference()
    {
        var annotation = new GdtAnnotationData("step-gdt-1", GdtControlType.Unknown, "◇", "0.2", "MMC", new[] { "A" }, "model.step", "pmi:17", "◇ 0.2 A", 17);

        Assert.Equal("pmi:17", annotation.SourceLocation);
        Assert.Equal(17, annotation.SourceOrdinal);
        Assert.Equal("MMC", annotation.Modifiers);
        Assert.Equal("model.step", annotation.SourceDocument);
    }
}
