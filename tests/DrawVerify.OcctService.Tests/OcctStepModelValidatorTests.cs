using DrawVerify.OcctService;
using Xunit;

namespace DrawVerify.OcctService.Tests;

public sealed class OcctStepModelValidatorTests
{
    private static string DatasetStep => Path.Combine(AppContext.BaseDirectory, "Dataset", "sample-000000", "000000.step");
    private static string InvalidStep => Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid.step");

    [Fact]
    public async Task Upload_stream_with_dataset_step_is_accepted_by_occt()
    {
        await using var content = File.OpenRead(DatasetStep);
        content.Position = 7;

        var result = await new OcctStepModelValidator().ValidateAsync(content);

        Assert.True(result.IsValid, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.Equal(7, content.Position);
    }

    [Fact]
    public async Task Upload_stream_with_invalid_fixture_is_rejected_with_occt_error_code()
    {
        await using var content = File.OpenRead(InvalidStep);

        var result = await new OcctStepModelValidator().ValidateAsync(content);

        Assert.False(result.IsValid);
        Assert.Equal("StepReadFailed", result.ErrorCode);
    }
}
