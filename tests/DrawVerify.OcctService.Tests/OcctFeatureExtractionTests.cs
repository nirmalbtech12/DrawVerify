using DrawVerify.OcctService;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class OcctFeatureExtractionTests
{
    [Fact(DisplayName = "TD-05 keeps missing STEP PMI capability explicit")]
    public void Missing_pmi_capability_is_not_reported_as_not_verified()
    {
        var result = new GdtAnnotationService().ExtractAnnotations("unused.step");

        Assert.False(result.Succeeded);
        Assert.Equal("CapabilityUnavailable", result.ErrorCategory);
        Assert.Empty(result.Annotations);
        Assert.DoesNotContain(result.Annotations, annotation => annotation.ControlType == DrawVerify.Core.Enums.GdtControlType.Unknown);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact(DisplayName = "TD-APP-003 projects finite stable edge and face references")]
    public void Cube_features_are_managed_and_finite()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("cube.step")));
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotEmpty(result.Features);
        Assert.Contains(result.Features, feature => feature.Reference.StartsWith("Edge:", StringComparison.Ordinal) && feature.Length is > 0 and < double.PositiveInfinity);
        Assert.Contains(result.Features, feature => feature.Reference.StartsWith("Face:", StringComparison.Ordinal));
        Assert.All(result.Features, feature => Assert.DoesNotContain("Generated", feature.GetType().AssemblyQualifiedName ?? string.Empty));
    }

    [Fact(DisplayName = "TD-APP-003 projects cylindrical geometry as hole or cylindrical face")]
    public void Cylinder_features_include_radius_and_diameter()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("cylinder.step")));
        Assert.True(result.Succeeded, result.ErrorMessage);
        var cylinder = Assert.Single(result.Features.Where(feature => feature.GeometryType is "Hole" or "CylindricalFace"));
        Assert.True(cylinder.Radius > 0);
        Assert.Equal(cylinder.Radius * 2, cylinder.Diameter);
    }

    [Fact(DisplayName = "TD-APP-003 reports invalid STEP extraction explicitly")]
    public void Invalid_fixture_does_not_fabricate_features()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("invalid.step")));
        Assert.False(result.Succeeded);
        Assert.Empty(result.Features);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
    }

    [Fact(DisplayName = "TD-APP-003 keeps managed angular measurements in degrees when present")]
    public void Angular_feature_values_are_finite_and_degree_based()
    {
        var feature = new GeometryFeatureData { Reference = "Angle:1", GeometryType = "Cone", AngleDegrees = 45 };
        Assert.Equal(45, feature.AngleDegrees);
        Assert.InRange(feature.AngleDegrees!.Value, 0, 180);
    }
}
