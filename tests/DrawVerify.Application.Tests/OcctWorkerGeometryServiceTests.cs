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
}
