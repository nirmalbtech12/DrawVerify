using DrawVerify.OcctService;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class OcctFeatureExtractionTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact(DisplayName = "TD-OCCT-001 maps finite feature values and optional STEP locations")]
    public void Extracted_features_have_stable_references_and_safe_metadata()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("cube.step")));
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.All(result.Features, feature =>
        {
            Assert.False(string.IsNullOrWhiteSpace(feature.Reference));
            Assert.All(new[] { feature.FeatureValue, feature.LocationX, feature.LocationY, feature.LocationZ }.Where(value => value.HasValue), value => Assert.True(double.IsFinite(value!.Value)));
            Assert.True(feature.Reference.StartsWith("Edge:", StringComparison.Ordinal) || feature.Reference.StartsWith("Face:", StringComparison.Ordinal));
        });
    }

    [Fact(DisplayName = "TD-OCCT-002 invalid STEP extraction remains a structured failure")]
    public void Invalid_step_has_no_fabricated_matchable_features()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("invalid.step")));

        Assert.False(result.Succeeded);
        Assert.Empty(result.Features);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
    }

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
