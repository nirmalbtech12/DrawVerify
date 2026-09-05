using System.Diagnostics;
using System.Text.Json;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.Services;

public sealed class OcctWorkerGeometryService : IGeometryService
{
    private readonly string _workerPath;
    private readonly long _maximumFileSizeBytes;
    private readonly TimeSpan _timeout;

    public OcctWorkerGeometryService(string workerPath, long maximumFileSizeBytes = 52428800, int timeoutSeconds = 30)
    {
        if (maximumFileSizeBytes <= 0 || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        _workerPath = workerPath;
        _maximumFileSizeBytes = maximumFileSizeBytes;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public MeasurementExtractionResult ExtractMeasurements(MeasurementExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.FilePath)) return MeasurementExtractionResult.Failure("FileNotFound", "Measurement extraction failed: stored STEP file was not found.");
        if (new FileInfo(request.FilePath).Length > _maximumFileSizeBytes) return MeasurementExtractionResult.Failure("FileTooLarge", "Measurement extraction failed: STEP file exceeds the configured maximum size.");
        using var process = new Process { StartInfo = new ProcessStartInfo(_workerPath) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        try
        {
            if (!process.Start()) return MeasurementExtractionResult.Failure("WorkerStart", "Measurement extraction failed: worker could not be started.");
            process.StandardInput.Write(JsonSerializer.Serialize(new { ProtocolVersion = 1, FilePath = request.FilePath }));
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); process.WaitForExit(); } catch { } return MeasurementExtractionResult.Failure("Timeout", "Measurement extraction failed: operation exceeded the configured timeout."); }
            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output)) return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned no result.");
            using var json = JsonDocument.Parse(output);
            var result = json.RootElement.GetProperty("Result").Deserialize<MeasurementExtractionResult>();
            return result ?? MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned an invalid result.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        { return MeasurementExtractionResult.Failure("Worker", $"Measurement extraction failed: {ex.Message}"); }
    }
}
