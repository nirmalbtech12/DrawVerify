using System.Diagnostics;
using System.Text.Json;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;
using DrawVerify.Core.Interfaces;
using ManagedGeometryService = DrawVerify.Geometry.IGeometryService;

namespace DrawVerify.Application.Services;

public sealed class OcctWorkerGeometryService : ManagedGeometryService, IMatchableGeometryService, IGdtAnnotationService
{
    private readonly string _workerPath;
    private readonly long _maximumFileSizeBytes;
    private readonly TimeSpan _timeout;
    private readonly ICentralizedLogger? _logger;

    public OcctWorkerGeometryService(string workerPath, long maximumFileSizeBytes = 52428800, int timeoutSeconds = 60, ICentralizedLogger? logger = null)
    {
        if (maximumFileSizeBytes <= 0 || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        _workerPath = workerPath;
        _maximumFileSizeBytes = maximumFileSizeBytes;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _logger = logger;
    }

    public GdtAnnotationExtractionResult ExtractAnnotations(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A STEP file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) return GdtAnnotationExtractionResult.Failure("FileNotFound", "GD&T extraction failed: stored STEP file was not found.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var process = new Process { StartInfo = new ProcessStartInfo(_workerPath) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        try
        {
            if (!process.Start()) return GdtAnnotationExtractionResult.Failure("WorkerStart", "GD&T extraction failed: worker could not be started.");
            process.StandardInput.Write(JsonSerializer.Serialize(new GdtAnnotationWorkerRequest(1, filePath)));
            process.StandardInput.Close();
            try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { Terminate(process, "GD&T worker", filePath); return GdtAnnotationExtractionResult.Failure("Timeout", "GD&T extraction failed: operation exceeded the configured timeout."); }
            var diagnostics = ReadDiagnostics(process);
            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output)) return GdtAnnotationExtractionResult.Failure("WorkerProtocol", $"GD&T extraction failed: worker returned no result.{FormatDiagnostics(diagnostics)}");
            var response = JsonSerializer.Deserialize<GdtAnnotationWorkerResponse>(output);
            if (response is null || response.ProtocolVersion != 1 || process.ExitCode != 0) return GdtAnnotationExtractionResult.Failure("WorkerProtocol", $"GD&T extraction failed: unsupported or failed worker response.{FormatDiagnostics(diagnostics)}");
            return response.Result;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            _logger?.Log(exception, "GD&T worker", $"Worker operation failed for file '{filePath}'.");
            return GdtAnnotationExtractionResult.Failure("Worker", $"GD&T extraction failed: {exception.Message}");
        }
    }

    public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested) return MatchableGeometryExtractionResult.Failure("Timeout", "Matching failed: operation timeout occurred before the worker was launched.");
        if (!File.Exists(request.FilePath)) return MatchableGeometryExtractionResult.Failure("FileNotFound", "Matching failed: stored STEP file was not found.");
        if (new FileInfo(request.FilePath).Length > _maximumFileSizeBytes) return MatchableGeometryExtractionResult.Failure("FileTooLarge", "Matching failed: STEP file exceeds the configured maximum size.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var process = new Process { StartInfo = new ProcessStartInfo(_workerPath) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        try
        {
            if (!process.Start()) return MatchableGeometryExtractionResult.Failure("WorkerStart", "Matching failed: worker could not be started.");
            if (timeout.IsCancellationRequested) return TimeoutResult(process, request.FilePath);
            process.StandardInput.Write(JsonSerializer.Serialize(new { ProtocolVersion = 1, Operation = "MatchableGeometry", FilePath = request.FilePath })); process.StandardInput.Close();
            try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { return TimeoutResult(process, request.FilePath); }
            if (timeout.IsCancellationRequested) return TimeoutResult(process, request.FilePath);
            var diagnosticsAfterExit = ReadDiagnostics(process); var output = process.StandardOutput.ReadToEnd(); if (string.IsNullOrWhiteSpace(output)) { LogDiagnostics("Matchable geometry worker", request.FilePath, diagnosticsAfterExit); return MatchableGeometryExtractionResult.Failure("WorkerProtocol", $"Matching failed: worker returned no result.{FormatDiagnostics(diagnosticsAfterExit)}"); }
            using var json = JsonDocument.Parse(output); var root = json.RootElement;
            if (!root.TryGetProperty("ProtocolVersion", out var version) || version.GetInt32() != 1 || process.ExitCode != 0) { LogDiagnostics("Matchable geometry worker", request.FilePath, diagnosticsAfterExit); return MatchableGeometryExtractionResult.Failure("WorkerProtocol", $"Matching failed: unsupported or failed worker response.{FormatDiagnostics(diagnosticsAfterExit)}"); }
            var result = root.TryGetProperty("Result", out var resultElement) ? resultElement.Deserialize<MatchableGeometryExtractionResult>() : null;
            return result ?? MatchableGeometryExtractionResult.Failure("WorkerProtocol", "Matching failed: worker returned an invalid result.");
        }
        catch (OperationCanceledException) { return TimeoutResult(process, request.FilePath); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException) { _logger?.Log(ex, "Matchable geometry worker", $"Worker operation failed for file '{request.FilePath}'."); return MatchableGeometryExtractionResult.Failure("Worker", $"Matching failed: {ex.Message}"); }
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
                var cleanupError = Terminate(process, "Measurement worker", request.FilePath);
                var timeoutDiagnostics = ReadDiagnostics(process);
                return MeasurementExtractionResult.Failure("Timeout", $"Measurement extraction failed: operation exceeded the configured timeout.{cleanupError}{FormatDiagnostics(timeoutDiagnostics)}");
            }
            var diagnostics = ReadDiagnostics(process);
            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output)) { LogDiagnostics("Measurement worker", request.FilePath, diagnostics); return MeasurementExtractionResult.Failure("WorkerProtocol", $"Measurement extraction failed: worker returned no result.{FormatDiagnostics(diagnostics)}"); }
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (!root.TryGetProperty("ProtocolVersion", out var version) || version.GetInt32() != 1)
                return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: unsupported worker protocol version.");
            if (process.ExitCode != 0)
            {
                LogDiagnostics("Measurement worker", request.FilePath, diagnostics);
                return MeasurementExtractionResult.Failure("WorkerExit", $"Measurement extraction failed: worker exited with code {process.ExitCode}. {SafeDiagnostics(diagnostics)}");
            }
            var result = root.TryGetProperty("Result", out var resultElement) ? resultElement.Deserialize<MeasurementExtractionResult>() : null;
            if (result is null) return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned an invalid result.");
            if (result.Succeeded && (result.Geometry is null || result.Geometry.BoundingBox is null || result.Geometry.Volume is not > 0))
                return MeasurementExtractionResult.Failure("WorkerProtocol", "Measurement extraction failed: worker returned an invalid successful result.");
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        { _logger?.Log(ex, "Measurement worker", $"Worker operation failed for file '{request.FilePath}'."); return MeasurementExtractionResult.Failure("Worker", $"Measurement extraction failed: {ex.Message}"); }
    }
    private string Terminate(Process process, string operation, string filePath)
    {
        try { if (!process.HasExited) process.Kill(true); process.WaitForExit(); return string.Empty; }
        catch (Exception exception) { _logger?.Log(exception, operation, $"Worker cleanup failed for file '{filePath}'."); return $" Worker termination failed: {exception.Message}"; }
    }
    private MatchableGeometryExtractionResult TimeoutResult(Process process, string filePath)
    {
        var cleanupError = Terminate(process, "Matchable geometry worker", filePath);
        var diagnostics = ReadDiagnostics(process);
        return MatchableGeometryExtractionResult.Failure("Timeout", $"Matching failed: operation exceeded the configured timeout.{cleanupError}{FormatDiagnostics(diagnostics)}");
    }
    private void LogDiagnostics(string operation, string filePath, string diagnostics) { if (!string.IsNullOrWhiteSpace(diagnostics)) _logger?.Log(new InvalidOperationException(SafeDiagnostics(diagnostics)), operation, $"Worker diagnostics for file '{filePath}'."); }
    private static string ReadDiagnostics(Process process) { try { return process.StandardError.ReadToEnd(); } catch (Exception exception) { return $"Unable to read worker diagnostics: {exception.Message}"; } }
    private static string FormatDiagnostics(string diagnostics) => string.IsNullOrWhiteSpace(diagnostics) ? string.Empty : $" Diagnostics: {SafeDiagnostics(diagnostics)}";
    private static string SafeDiagnostics(string diagnostics) => string.IsNullOrWhiteSpace(diagnostics) ? "No worker diagnostics." : diagnostics.Length > 512 ? diagnostics[..512] : diagnostics;
    private sealed record GdtAnnotationWorkerRequest(int ProtocolVersion, string FilePath) { }
    private sealed record GdtAnnotationWorkerResponse(int ProtocolVersion, GdtAnnotationExtractionResult Result) { }
}
