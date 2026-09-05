using DrawVerify.Geometry.Models;
using DrawVerify.OcctService;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class OcctMeasurementExtractionTests
{
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
