using DrawVerify.Geometry.Models;

namespace DrawVerify.Geometry;

public interface IProjectionDrawingService
{
    ProjectionDrawing Generate(string filePath, ProjectionDrawingView view, CancellationToken cancellationToken = default);
}
