using DrawVerify.OcctService;

namespace DrawVerify.OcctWorker;

public static class Program
{
    public static int Main()
    {
        try
        {
            var request = WorkerRequest.Parse(Console.In.ReadToEnd());
            if (request.ProtocolVersion != 1) throw new InvalidDataException("Unsupported worker protocol version.");
            var response = new MeasurementExtractionWorker(new OcctGeometryService()).Execute(request);
            Console.Out.Write(response.Serialize());
            return response.Result.Succeeded ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.Write(ex.Message);
            return 1;
        }
    }
}
