using System.Text.Json;
using DrawVerify.Geometry.Models;

namespace DrawVerify.OcctWorker;

public sealed record WorkerRequest(int ProtocolVersion, string FilePath)
{
    public static WorkerRequest Parse(string json) => JsonSerializer.Deserialize<WorkerRequest>(json) ?? throw new InvalidDataException("Worker request is missing.");
}
public sealed record MatchableGeometryWorkerRequest(int ProtocolVersion, string FilePath, string Operation);
public sealed record MatchableGeometryWorkerResponse(int ProtocolVersion, MatchableGeometryExtractionResult Result)
{
    public string Serialize() => JsonSerializer.Serialize(this);
}
public sealed record WorkerResponse(int ProtocolVersion, MeasurementExtractionResult Result)
{
    public string Serialize() => JsonSerializer.Serialize(this);
    public static WorkerResponse Parse(string json) => JsonSerializer.Deserialize<WorkerResponse>(json) ?? throw new InvalidDataException("Worker response is missing.");
}
public sealed record GdtAnnotationWorkerRequest(int ProtocolVersion, string FilePath);
public sealed record GdtAnnotationWorkerResponse(int ProtocolVersion, GdtAnnotationExtractionResult Result)
{
    public string Serialize() => JsonSerializer.Serialize(this);
}
