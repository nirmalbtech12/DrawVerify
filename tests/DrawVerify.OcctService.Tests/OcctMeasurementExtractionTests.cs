using DrawVerify.Geometry.Models;
using DrawVerify.OcctService;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class OcctMeasurementExtractionTests
{
    [Fact(DisplayName = "TD-OCCT-002 invalid measurement input has structured visible failure")]
    public void Invalid_measurement_input_does_not_fabricate_success()
    {
        var result = new OcctGeometryService().ExtractMeasurements(new MeasurementExtractionRequest(Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid.step")));

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCategory));
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact(DisplayName = "TD-OCCT-001 returns categorized failure for invalid STEP")]
    public void Invalid_step_is_not_reported_as_success()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid.step");
        var result = new OcctGeometryService().ExtractMeasurements(new MeasurementExtractionRequest(path));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorCategory);
        Assert.NotNull(result.ErrorMessage);
    }
}
