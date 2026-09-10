using DrawVerify.Application.Verification;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

/// <summary>
/// Tests for GdtValidator against the documented MVP GD&T subset.
/// </summary>
public class GdtValidatorTests
{
    [Theory(DisplayName = "TD-02 dispatches the documented four-control subset")]
    [InlineData(GdtControlType.Position)]
    [InlineData(GdtControlType.Flatness)]
    [InlineData(GdtControlType.Parallelism)]
    [InlineData(GdtControlType.Perpendicularity)]
    public void Documented_control_is_validated_as_supported(GdtControlType type)
    {
        var validator = new GdtValidator();
        var result = validator.Validate(Annotation(type, type.ToString()));

        Assert.Contains(type, validator.SupportedControls);
        Assert.Equal(VerificationStatus.Passed, result.Status);
        Assert.Null(result.Reason);
    }

    [Fact(DisplayName = "TD-02 flags unsupported and unknown controls without coercion")]
    public void Unsupported_control_is_not_verified()
    {
        var result = new GdtValidator().Validate(Annotation(GdtControlType.Unknown, "◇"));

        Assert.Equal(GdtControlType.Unknown, result.ControlType);
        Assert.Equal(VerificationStatus.NotVerified, result.Status);
        Assert.Equal(GdtResult.UnsupportedReason, result.Reason);
        Assert.DoesNotContain(result.Status, new[] { VerificationStatus.Matched, VerificationStatus.Passed, VerificationStatus.Failed, VerificationStatus.Missing, VerificationStatus.Extra });
    }

    [Fact(DisplayName = "TD-02 rejects duplicate controls in subset files")]
    public void Subset_file_rejects_duplicate_controls()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"gdtSubset\":{\"version\":1,\"controls\":[{\"type\":\"Position\"},{\"type\":\"Position\"}]}}");
            Assert.Throws<InvalidDataException>(() => GdtValidator.FromSubsetFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact(DisplayName = "TD-11 fails visibly for malformed or unsupported subset configuration")]
    public void Malformed_subset_configuration_is_not_silently_defaulted()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"gdtSubset\":{\"version\":99,\"controls\":[]}}");
            var exception = Assert.Throws<InvalidDataException>(() => GdtValidator.FromSubsetFile(path));
            Assert.Contains("unsupported schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    private static GdtAnnotationData Annotation(GdtControlType type, string symbol) =>
        new("annotation-1", type, symbol, "0.1", "MMC", new[] { "A" }, "drawing.dxf", "entity:7", symbol, 7);
}
