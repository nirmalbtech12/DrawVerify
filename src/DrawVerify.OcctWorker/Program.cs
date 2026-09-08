using System.Text.Json;
using DrawVerify.OcctService;

namespace DrawVerify.OcctWorker;

public static class Program
{
    public static int Main()
    {
        try
        {
            var json = Console.In.ReadToEnd();
            using var parsed = JsonDocument.Parse(json);
            var operation = parsed.RootElement.TryGetProperty("Operation", out var operationValue) ? operationValue.GetString() : null;
            if (string.Equals(operation, "MatchableGeometry", StringComparison.Ordinal))
            {
                var request = JsonSerializer.Deserialize<MatchableGeometryWorkerRequest>(json) ?? throw new InvalidDataException("Worker request is missing.");
                if (request.ProtocolVersion != 1) throw new InvalidDataException("Unsupported worker protocol version.");
                var response = new MatchableGeometryWorker(new OcctGeometryService()).Execute(request);
                Console.Out.Write(response.Serialize());
                return response.Result.Succeeded ? 0 : 2;
            }
            var measurement = WorkerRequest.Parse(json);
            if (measurement.ProtocolVersion != 1) throw new InvalidDataException("Unsupported worker protocol version.");
            var result = new MeasurementExtractionWorker(new OcctGeometryService()).Execute(measurement);
            Console.Out.Write(result.Serialize());
            return result.Result.Succeeded ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.Write(ex.Message);
            return 1;
        }
    }
}
