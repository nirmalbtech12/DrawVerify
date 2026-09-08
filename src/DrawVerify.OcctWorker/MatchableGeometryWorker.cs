using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.OcctWorker;

public sealed class MatchableGeometryWorker(IMatchableGeometryService geometry)
{
    public MatchableGeometryWorkerResponse Execute(MatchableGeometryWorkerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request.ProtocolVersion, geometry.ExtractMatchableGeometry(new MatchableGeometryExtractionRequest(request.FilePath), cancellationToken));
    }
}
