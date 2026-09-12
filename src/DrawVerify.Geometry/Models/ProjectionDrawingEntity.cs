namespace DrawVerify.Geometry.Models;

public enum ProjectionDrawingEntityType { Line, Arc, Circle, Polyline }

public sealed record ProjectionDrawingEntity(
    ProjectionDrawingEntityType Type,
    IReadOnlyList<double> Coordinates,
    bool Hidden = false);
