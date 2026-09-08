using DrawVerify.OcctService;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class OcctFeatureExtractionTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact(DisplayName = "TD-07 projects finite stable edge and face references")]
    public void Cube_features_are_managed_and_finite()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("cube.step")));
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotEmpty(result.Features);
        Assert.Contains(result.Features, feature => feature.Reference.StartsWith("Edge:", StringComparison.Ordinal) && feature.Length is > 0 and < double.PositiveInfinity);
        Assert.Contains(result.Features, feature => feature.Reference.StartsWith("Face:", StringComparison.Ordinal));
        Assert.All(result.Features, feature => Assert.DoesNotContain("Generated", feature.GetType().AssemblyQualifiedName ?? string.Empty));
    }

    [Fact(DisplayName = "TD-07 projects cylindrical geometry as hole or cylindrical face")]
    public void Cylinder_features_include_radius_and_diameter()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("cylinder.step")));
        Assert.True(result.Succeeded, result.ErrorMessage);
        var cylinder = Assert.Single(result.Features.Where(feature => feature.GeometryType is "Hole" or "CylindricalFace"));
        Assert.True(cylinder.Radius > 0);
        Assert.Equal(cylinder.Radius * 2, cylinder.Diameter);
    }

    [Fact(DisplayName = "TD-07 reports invalid STEP extraction explicitly")]
    public void Invalid_fixture_does_not_fabricate_features()
    {
        var result = new OcctGeometryService().ExtractMatchableGeometry(new DrawVerify.Geometry.Models.MatchableGeometryExtractionRequest(Fixture("invalid.step")));
        Assert.False(result.Succeeded);
        Assert.Empty(result.Features);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
    }
}
