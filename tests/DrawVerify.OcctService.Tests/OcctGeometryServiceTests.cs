using DrawVerify.Occt;
using Xunit;

namespace DrawVerify.OcctService.Tests;

/// <summary>
/// Integration tests against the real OCCT DLLs. Requires the OCCT native
/// build (native/bin) to be present — NOT run in lightweight CI.
/// </summary>
public class OcctGeometryServiceTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "screw.step");
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static string DatasetStep(int id) => Path.Combine(AppContext.BaseDirectory, "Dataset", $"sample-{id:000000}", $"{id:000000}.step");
    private static string DatasetDxf(int id) => Path.Combine(AppContext.BaseDirectory, "Dataset", $"sample-{id:000000}", $"{id:000000}.dxf");

    [Fact]
    public void Runtime_reports_real_occt_capabilities()
    {
        var status = new OcctRuntime().CheckReadiness();

        Assert.True(status.IsReady, $"{status.ErrorCode}: {status.Message}");
        Assert.StartsWith("8.0", status.OcctVersion);
        Assert.True(status.IsStepSupported);
        Assert.True(status.IsProjectionSupported);
        Assert.True(status.IsMeshingSupported);
    }

    [Fact]
    public void Public_api_loads_queries_projects_and_meshes_step()
    {
        using var model = new OcctModelLoader().LoadStep(FixturePath);

        var summary = model.GetSummary();
        Assert.False(model.IsNull);
        Assert.True(summary.VertexCount > 0);
        Assert.True(summary.EdgeCount > 0);
        Assert.True(summary.FaceCount > 0);
        Assert.Equal("millimeter", summary.Unit);
        Assert.Equal(1d, summary.UnitScaleToMillimeters, 12);
        Assert.True(model.Validate().IsValid);
        Assert.NotEmpty(model.GetEdges());
        Assert.NotEmpty(model.GetFaces());
        Assert.NotEmpty(model.GenerateProjection(OcctProjectionView.Front).VisibleLines);
        var mesh = model.GenerateMeshData();
        Assert.NotEmpty(mesh.Triangles);
        Assert.Contains(mesh.Vertices, vertex => Math.Abs(vertex.Normal.X) + Math.Abs(vertex.Normal.Y) + Math.Abs(vertex.Normal.Z) > 0.99);
    }

    [Fact]
    public void Downloaded_dataset_pairs_load_in_one_round()
    {
        var loader = new OcctModelLoader();
        foreach (var id in new[] { 0, 1 })
        {
            Assert.True(File.Exists(DatasetDxf(id)), $"Missing paired DXF for sample {id}");
            using var model = loader.LoadStep(DatasetStep(id));
            var summary = model.GetSummary();
            Assert.False(model.IsNull);
            Assert.True(summary.VertexCount > 0);
            Assert.True(summary.EdgeCount > 0);
            Assert.True(summary.FaceCount > 0);
            Assert.True(model.Validate().IsValid);
            Assert.NotEmpty(model.GenerateProjection(OcctProjectionView.Front).VisibleLines);
        }
    }

    [Fact]
    public void Missing_step_file_propagates_categorized_error()
    {
        var error = Assert.Throws<OcctOperationException>(() => new OcctModelLoader().LoadStep(FixturePath + ".missing"));

        Assert.Equal("FileNotFound", error.ErrorCode);
        var diagnostic = Assert.Single(error.Diagnostics);
        Assert.Equal("LoadStep", diagnostic.Operation);
    }

    [Fact]
    public void Invalid_step_content_propagates_read_failure()
    {
        var error = Assert.Throws<OcctOperationException>(() => new OcctModelLoader().LoadStep(Fixture("invalid.step")));

        Assert.Equal("StepReadFailed", error.ErrorCode);
        Assert.Equal("ReadFile", Assert.Single(error.Diagnostics).Operation);
    }

    [Fact]
    public void Disposed_model_rejects_further_native_operations()
    {
        var model = new OcctModelLoader().LoadStep(FixturePath);
        model.Dispose();

        Assert.Throws<ObjectDisposedException>(() => model.GetSummary());
    }

    [Fact]
    public void Geometry_service_is_real_occt_backed_boundary()
    {
        var service = new DrawVerify.OcctService.OcctGeometryService();
        var status = service.CheckRuntime();
        Assert.True(status.IsReady, $"{status.ErrorCode}: {status.Message}");

        using var model = service.LoadStep(FixturePath);
        Assert.True(model.GetSummary().FaceCount > 0);
    }

    [Fact]
    public void Cube_fixture_has_expected_exact_topology_and_properties()
    {
        using var model = new OcctModelLoader().LoadStep(Fixture("cube.step"));

        var summary = model.GetSummary();
        Assert.Equal(8, summary.VertexCount);
        Assert.Equal(12, summary.EdgeCount);
        Assert.Equal(6, summary.FaceCount);
        Assert.Equal(600d, summary.SurfaceArea, 8);
        Assert.Equal(1000d, summary.Volume, 8);
        Assert.All(model.GetEdges(), edge => Assert.Equal("Line", edge.GeometryType));
        Assert.All(model.GetFaces(), face => Assert.Equal("Plane", face.SurfaceType));
    }

    [Fact]
    public void Cylinder_fixture_exposes_circular_edges_and_cylindrical_face()
    {
        using var model = new OcctModelLoader().LoadStep(Fixture("cylinder.step"));

        var summary = model.GetSummary();
        Assert.Equal(3, summary.FaceCount);
        Assert.Equal(Math.PI * 150d, summary.SurfaceArea, 7);
        Assert.Equal(Math.PI * 250d, summary.Volume, 7);
        Assert.Contains(model.GetEdges(), edge => edge.GeometryType == "Circle");
        Assert.Contains(model.GetFaces(), face => face.SurfaceType == "Cylinder" && Math.Abs(face.Radius - 5d) < 1e-9);
    }

    [Theory]
    [InlineData("plate-through-hole.step")]
    [InlineData("stepped-block.step")]
    public void Generated_fixture_loads_valid_nonempty_geometry(string fixtureName)
    {
        using var model = new OcctModelLoader().LoadStep(Fixture(fixtureName));

        var summary = model.GetSummary();
        Assert.True(model.Validate().IsValid);
        Assert.True(summary.VertexCount > 0);
        Assert.True(summary.EdgeCount > 0);
        Assert.True(summary.FaceCount > 0);
        Assert.True(summary.Volume > 0);
    }
}
