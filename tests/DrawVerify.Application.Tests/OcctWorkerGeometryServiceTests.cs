using DrawVerify.Application.Services;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class OcctWorkerGeometryServiceTests
{
    [Fact(DisplayName = "TD-APP-002 rejects missing files before worker launch")]
    public void Missing_file_is_a_clear_failure()
    {
        var service = new OcctWorkerGeometryService("worker-that-does-not-exist", timeoutSeconds: 1);
        var result = service.ExtractMeasurements(new MeasurementExtractionRequest(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".step")));

        Assert.False(result.Succeeded);
        Assert.Equal("FileNotFound", result.ErrorCategory);
        Assert.Contains("stored STEP file", result.ErrorMessage);
    }

    [Fact(DisplayName = "TD-APP-002 rejects files over configured maximum")]
    public void Oversized_file_is_rejected_before_worker_launch()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[8]);
            var result = new OcctWorkerGeometryService("worker-that-does-not-exist", maximumFileSizeBytes: 7, timeoutSeconds: 1)
                .ExtractMeasurements(new MeasurementExtractionRequest(path));

            Assert.False(result.Succeeded);
            Assert.Equal("FileTooLarge", result.ErrorCategory);
            Assert.Contains("configured maximum", result.ErrorMessage);
        }
        finally { File.Delete(path); }
    }

    [Fact(DisplayName = "TD-APP-002 validates timeout configuration")]
    public void Non_positive_timeout_configuration_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcctWorkerGeometryService("worker", timeoutSeconds: 0));
    }

    [Fact(DisplayName = "TD-06 rejects missing matchable geometry files before worker launch")]
    public void Matchable_geometry_missing_file_is_a_clear_failure()
    {
        var result = new OcctWorkerGeometryService("worker-that-does-not-exist", timeoutSeconds: 1)
            .ExtractMatchableGeometry(new MatchableGeometryExtractionRequest(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".step")));
        Assert.False(result.Succeeded);
        Assert.Equal("FileNotFound", result.ErrorCode);
    }

    [Fact(DisplayName = "TD-06 rejects oversized matchable geometry files")]
    public void Matchable_geometry_oversized_file_is_rejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[8]);
            var result = new OcctWorkerGeometryService("worker-that-does-not-exist", maximumFileSizeBytes: 7, timeoutSeconds: 1)
                .ExtractMatchableGeometry(new MatchableGeometryExtractionRequest(path));
            Assert.False(result.Succeeded);
            Assert.Equal("FileTooLarge", result.ErrorCode);
        }
        finally { File.Delete(path); }
    }

    [Fact(DisplayName = "TD-06 cancellation terminates the worker and returns a timeout failure")]
    public void Cancelled_matchable_geometry_request_terminates_worker()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "step");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = new OcctWorkerGeometryService("cmd.exe", timeoutSeconds: 60)
                .ExtractMatchableGeometry(new MatchableGeometryExtractionRequest(path), cancellation.Token);

            Assert.False(result.Succeeded);
            Assert.Equal("Timeout", result.ErrorCode);
            Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact(DisplayName = "TD-06 timeout configuration is bounded and cancellation cleanup is repeatable")]
    public void Timeout_cleanup_can_be_repeated_without_leaking_worker_state()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "step");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var result = new OcctWorkerGeometryService("cmd.exe", timeoutSeconds: 1)
                    .ExtractMatchableGeometry(new MatchableGeometryExtractionRequest(path), cancellation.Token);
                Assert.Equal("Timeout", result.ErrorCode);
            }
        }
        finally { File.Delete(path); }
    }
}
