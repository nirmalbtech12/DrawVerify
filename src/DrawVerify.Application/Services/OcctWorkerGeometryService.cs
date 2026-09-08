using System.Diagnostics;
using System.Text.Json;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.Services;

public sealed class OcctWorkerGeometryService : IGeometryService, IMatchableGeometryService
{
    private readonly string _workerPath;
    private readonly long _maximumFileSizeBytes;
    private readonly TimeSpan _timeout;

    public OcctWorkerGeometryService(string workerPath, long maximumFileSizeBytes = 52428800, int timeoutSeconds = 60)
    {
        if (maximumFileSizeBytes <= 0 || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        _workerPath = workerPath;
        _maximumFileSizeBytes = maximumFileSizeBytes;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.FilePath)) return MatchableGeometryExtractionResult.Failure("FileNotFound", "Matching failed: stored STEP file was not found.");
        if (new FileInfo(request.FilePath).Length > _maximumFileSizeBytes) return MatchableGeometryExtractionResult.Failure("FileTooLarge", "Matching failed: STEP file exceeds the configured maximum size.");
        using var process = new Process { StartInfo = new ProcessStartInfo(_workerPath) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        try
        {
            if (!process.Start()) return MatchableGeometryExtractionResult.Failure("WorkerStart", "Matching failed: worker could not be started.");
            process.StandardInput.Write(JsonSerializer.Serialize(new { ProtocolVersion = 1, Operation = "MatchableGeometry", FilePath = request.FilePath })); process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(_timeout);
            try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); process.WaitForExit(); } catch { } return MatchableGeometryExtractionResult.Failure("Timeout", "Matching failed: operation exceeded the configured timeout."); }
            var output = process.StandardOutput.ReadToEnd(); if (string.IsNullOrWhiteSpace(output)) return MatchableGeometryExtractionResult.Failure("WorkerProtocol", "Matching failed: worker returned no result.");
            using var json = JsonDocument.Parse(output); var root = json.RootElement;
            if (!root.TryGetProperty("ProtocolVersion", out var version) || version.GetInt32() != 1 || process.ExitCode != 0) return MatchableGeometryExtractionResult.Failure("WorkerProtocol", "Matching failed: unsupported or failed worker response.");
            var result = root.TryGetProperty("Result", out var resultElement) ? resultElement.Deserialize<MatchableGeometryExtractionResult>() : null;
            return result ?? MatchableGeometryExtractionResult.Failure("WorkerProtocol", "Matching failed: worker returned an invalid result.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException) { return MatchableGeometryExtractionResult.Failure("Worker", $"Matching failed: {ex.Message}"); }
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
            catch (OperationCanceledException)
            {
                var cleanupError = string.Empty;
                try { if (!process.HasExited) process.Kill(true); process.WaitForExit(); }
                catch (Exception cleanupException) { cleanupError = $" Worker termination failed: {cleanupException.Message}"; }
                return MeasurementExtractionResult.Failure("Timeout", $"Measurement extraction failed: operation exceeded the configured timeout.{cleanupError}");
            }
            var diagnostics = process.StandardError.ReadToEnd();
            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output)) return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned no result.");
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (!root.TryGetProperty("ProtocolVersion", out var version) || version.GetInt32() != 1)
                return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: unsupported worker protocol version.");
            if (process.ExitCode != 0)
                return MeasurementExtractionResult.Failure("WorkerExit", $"Measurement extraction failed: worker exited with code {process.ExitCode}. {SafeDiagnostics(diagnostics)}");
            var result = root.TryGetProperty("Result", out var resultElement) ? resultElement.Deserialize<MeasurementExtractionResult>() : null;
            if (result is null) return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned an invalid result.");
            if (result.Succeeded && (result.Geometry is null || result.Geometry.BoundingBox is null || result.Geometry.Volume is not > 0))
                return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned an invalid successful result.");
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        { return MeasurementExtractionResult.Failure("Worker", $"Measurement extraction failed: {ex.Message}"); }
    }
    private static string SafeDiagnostics(string diagnostics) => string.IsNullOrWhiteSpace(diagnostics) ? "No worker diagnostics." : diagnostics.Length > 512 ? diagnostics[..512] : diagnostics;
}
