using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.OcctWorker;

public sealed class MeasurementExtractionWorker(IGeometryService geometry)
{
    public WorkerResponse Execute(WorkerRequest request, CancellationToken cancellationToken = default) =>
        new(request.ProtocolVersion, geometry.ExtractMeasurements(new MeasurementExtractionRequest(request.FilePath), cancellationToken));
}
