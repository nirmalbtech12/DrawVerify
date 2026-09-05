using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class MeasurementDomainTests
{
    [Fact(DisplayName = "TD-CORE-001 preserves solid measurements and nullable tolerances")]
    public void Solid_measurement_preserves_measurements_and_feature_dimensions()
    {
        var versionId = Guid.NewGuid();
        var geometry = new GeometryMeasurement(versionId, 1000, 0, 1, 2, 10, 21, 32, 6, 12, 8, "mm");
        var feature = new FeatureMeasurement(versionId, "Hole", new Dictionary<string, double> { ["Diameter"] = 4 }, "mm");

        Assert.Equal(1000, geometry.Volume);
        Assert.Equal(10, geometry.Length);
        Assert.Equal(20, geometry.Width);
        Assert.Equal(30, geometry.Height);
        Assert.Null(feature.LowerTolerance);
        Assert.Equal(versionId, feature.DocumentVersionId);
    }

    [Fact(DisplayName = "TD-CORE-001 represents partial non-solid data without invented volume")]
    public void Partial_measurement_preserves_counts_and_error_state()
    {
        var version = new DocumentVersion { GeometryStatus = GeometryStatus.Error, GeometryError = "Model does not contain a valid solid" };
        version.Geometry = new GeometryMeasurement(version.Id, null, 0, 0, 0, 5, 6, 7, 1, 2, 3, "mm");

        Assert.Equal(GeometryStatus.Error, version.GeometryStatus);
        Assert.Null(version.Geometry.Volume);
        Assert.Equal(3, version.Geometry.VertexCount);
        Assert.Equal("Model does not contain a valid solid", version.GeometryError);
    }
}
