using DrawVerify.Geometry.Models;
namespace DrawVerify.Geometry;
public interface IMatchableGeometryService
{
    MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default);
}
