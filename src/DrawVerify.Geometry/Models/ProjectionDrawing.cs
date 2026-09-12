namespace DrawVerify.Geometry.Models;

public enum ProjectionDrawingView { Front, Rear, Top, Bottom, Left, Right }

public sealed record ProjectionDrawing(
    ProjectionDrawingView View,
    IReadOnlyList<ProjectionDrawingEntity> Entities,
    IReadOnlyList<ProjectionDrawingDimension> Dimensions,
    double ApproximationTolerance,
    IReadOnlyList<string> Diagnostics)
{
    public int EntityCount => Entities.Count;
    public int DimensionCount => Dimensions.Count;
}
