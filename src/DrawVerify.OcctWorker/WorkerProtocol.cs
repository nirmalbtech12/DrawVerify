using System.Text.Json;
using DrawVerify.Geometry.Models;

namespace DrawVerify.OcctWorker;

public sealed record WorkerRequest(int ProtocolVersion, string FilePath)
{
    public static WorkerRequest Parse(string json) => JsonSerializer.Deserialize<WorkerRequest>(json) ?? throw new InvalidDataException("Worker request is missing.");
}
public sealed record WorkerResponse(int ProtocolVersion, MeasurementExtractionResult Result)
{
    public string Serialize() => JsonSerializer.Serialize(this);
    public static WorkerResponse Parse(string json) => JsonSerializer.Deserialize<WorkerResponse>(json) ?? throw new InvalidDataException("Worker response is missing.");
}
