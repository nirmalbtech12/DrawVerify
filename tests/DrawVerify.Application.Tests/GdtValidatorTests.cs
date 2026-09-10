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
    [Theory(DisplayName = "TD-KAN44-002 dispatches exactly the documented four-control subset")]
    [InlineData(GdtControlType.Position)]
    [InlineData(GdtControlType.Flatness)]
    [InlineData(GdtControlType.Parallelism)]
    [InlineData(GdtControlType.Perpendicularity)]
    public void Documented_control_is_validated_as_supported(GdtControlType type)
    {
        var validator = new GdtValidator();
        var result = Assert.Single(validator.Validate(new[] { Annotation(type, type.ToString()) }, new[] { Annotation(type, type.ToString()) }));

        Assert.Contains(type, validator.SupportedControls);
        Assert.Equal(VerificationStatus.Passed, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal(GdtResult.CurrentRuleVersion, result.RuleVersion);
    }

    [Fact(DisplayName = "TD-KAN44-002 flags unsupported and unknown controls without coercion")]
    public void Unsupported_control_is_not_verified()
    {
        var result = Assert.Single(new GdtValidator().Validate(new[] { Annotation(GdtControlType.Unknown, "◇") }, new[] { Annotation(GdtControlType.Unknown, "◇") }));

        Assert.Equal(GdtControlType.Unknown, result.ControlType);
        Assert.Equal(VerificationStatus.NotVerified, result.Status);
        Assert.Equal(GdtResult.UnsupportedReason, result.Reason);
        Assert.DoesNotContain(result.Status, new[] { VerificationStatus.Matched, VerificationStatus.Passed, VerificationStatus.Failed, VerificationStatus.Missing, VerificationStatus.Extra });
    }

    [Fact(DisplayName = "TD-KAN44-002 rejects duplicate controls and malformed rule versions")]
    public void Subset_file_rejects_duplicate_controls()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{\"gdtSubset\":{\"version\":1,\"ruleVersion\":\"not-a-version\",\"controls\":[{\"type\":\"Position\"},{\"type\":\"Position\"}]}}");
        Assert.Throws<InvalidDataException>(() => GdtValidator.FromSubsetFile(path));
        // FromSubsetFile currently leaves its read stream open; cleanup is deferred to process teardown.
    }

    [Fact(DisplayName = "TD-KAN44-003 compares position fields and ordered datums")]
    public void Position_mismatch_is_reported_with_comparison_details()
    {
        var step = Annotation(GdtControlType.Position, "⌖", feature: "hole-1", nominal: "10,20", tolerance: "0.1", zone: "diameter", diameter: "0.2", modifiers: "MMC", datums: new[] { "A", "B" });
        var dxf = Annotation(GdtControlType.Position, "⌖", feature: "hole-1", nominal: "10,20", tolerance: "0.1", zone: "diameter", diameter: "0.2", modifiers: "MMC", datums: new[] { "B", "A" });

        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));

        Assert.Equal(VerificationStatus.Mismatched, result.Status);
        Assert.Contains("datum reference order", result.ComparisonDetails);
        Assert.Equal("1.0.0", result.RuleVersion);
    }

    [Theory(DisplayName = "TD-KAN44-003 detects every required position comparison field")]
    [InlineData("feature")]
    [InlineData("nominal")]
    [InlineData("tolerance")]
    [InlineData("zone")]
    [InlineData("diameter")]
    [InlineData("modifiers")]
    public void Position_required_field_difference_is_not_passed(string field)
    {
        var step = Annotation(GdtControlType.Position, "⌖", feature: "hole-1", nominal: "10,20", tolerance: "0.1", zone: "diameter", diameter: "0.2", modifiers: "MMC", datums: new[] { "A" });
        var dxf = step with
        {
            ControlledFeature = field == "feature" ? "hole-2" : step.ControlledFeature,
            NominalBasicLocation = field == "nominal" ? "10,21" : step.NominalBasicLocation,
            ToleranceValue = field == "tolerance" ? "0.2" : step.ToleranceValue,
            ToleranceZoneType = field == "zone" ? "width" : step.ToleranceZoneType,
            ToleranceZoneDiameter = field == "diameter" ? "0.3" : step.ToleranceZoneDiameter,
            Modifiers = field == "modifiers" ? "LMC" : step.Modifiers,
            NormalizedModifiers = field == "modifiers" ? "LMC" : step.NormalizedModifiers
        };

        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));

        Assert.Equal(VerificationStatus.Mismatched, result.Status);
        var expectedField = field switch
        {
            "feature" => "controlled feature",
            "nominal" => "nominal/basic location",
            "tolerance" => "tolerance",
            "zone" or "diameter" => "tolerance zone",
            "modifiers" => "modifiers",
            _ => field
        };
        Assert.Contains(expectedField, result.ComparisonDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "TD-KAN44-003 records geometric criterion outcomes")]
    [InlineData("within tolerance", VerificationStatus.Passed)]
    [InlineData("outside tolerance", VerificationStatus.Failed)]
    [InlineData("criterion unavailable", VerificationStatus.NotVerified)]
    public void Position_criterion_maps_to_defined_status(string criterion, VerificationStatus expected)
    {
        var step = Annotation(GdtControlType.Position, "⌖", nominal: "10,20") with { ComparisonCriteria = criterion };
        var dxf = step with { SourceDocument = "drawing.dxf", SourceLocation = "entity:8" };

        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));

        Assert.Equal(expected, result.Status);
        Assert.Equal("1.0.0", result.RuleVersion);
    }

    [Fact(DisplayName = "TD-KAN44-003 accepts a unique feature fallback across differing source identifiers")]
    public void Unique_feature_fallback_accepts_different_step_and_dxf_identifiers()
    {
        var step = Annotation(GdtControlType.Position, "⌖", feature: "hole-1") with { Identifier = "step-position-1", SourcePairId = "pair-1" };
        var dxf = Annotation(GdtControlType.Position, "⌖", feature: "hole-1") with { Identifier = "dxf-position-9", SourcePairId = "pair-1", SourceDocument = "drawing.dxf" };

        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));

        Assert.Equal(VerificationStatus.Passed, result.Status);
    }

    [Fact(DisplayName = "TD-KAN44-004 validates datum-free flatness and detects surface mismatch")]
    public void Flatness_does_not_require_datum()
    {
        var step = Annotation(GdtControlType.Flatness, "⏥", feature: "surface-1", tolerance: "0.05", datums: Array.Empty<string>());
        var dxf = Annotation(GdtControlType.Flatness, "⏥", feature: "surface-1", tolerance: "0.05", datums: Array.Empty<string>());
        Assert.Equal(VerificationStatus.Passed, Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf })).Status);

        var mismatch = Annotation(GdtControlType.Flatness, "⏥", feature: "surface-2", tolerance: "0.05", datums: Array.Empty<string>());
        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { mismatch }));
        Assert.Equal(VerificationStatus.Mismatched, result.Status);
        Assert.Contains("controlled feature", result.ComparisonDetails);
    }

    [Fact(DisplayName = "TD-KAN44-004 detects flatness tolerance mismatch without requiring a datum")]
    public void Flatness_tolerance_difference_is_mismatch_without_datum()
    {
        var step = Annotation(GdtControlType.Flatness, "⏥", feature: "surface-1", tolerance: "0.05", datums: Array.Empty<string>());
        var dxf = step with { ToleranceValue = "0.10", SourceDocument = "drawing.dxf" };

        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));

        Assert.Equal(VerificationStatus.Mismatched, result.Status);
        Assert.Contains("tolerance", result.ComparisonDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "TD-KAN44-005 compares orientation controls and datum order")]
    [InlineData(GdtControlType.Parallelism)]
    [InlineData(GdtControlType.Perpendicularity)]
    public void Orientation_control_mismatch_is_not_passed(GdtControlType type)
    {
        var step = Annotation(type, type.ToString(), feature: "face-1", tolerance: "0.1", zone: "diameter", modifiers: "MMC", datums: new[] { "A", "B" });
        var dxf = Annotation(type, type.ToString(), feature: "face-1", tolerance: "0.1", zone: "diameter", modifiers: "MMC", datums: new[] { "B", "A" });
        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));
        Assert.Equal(VerificationStatus.Mismatched, result.Status);
        Assert.Contains("datum reference order", result.ComparisonDetails);
    }

    [Theory(DisplayName = "TD-KAN44-005 detects orientation tolerance, zone, and modifier differences")]
    [InlineData("tolerance")]
    [InlineData("zone")]
    [InlineData("modifiers")]
    public void Orientation_required_field_difference_is_mismatch(string field)
    {
        var step = Annotation(GdtControlType.Parallelism, "∥", feature: "face-1", tolerance: "0.1", zone: "width", modifiers: "MMC", datums: new[] { "A" });
        var dxf = step with
        {
            SourceDocument = "drawing.dxf",
            ToleranceValue = field == "tolerance" ? "0.2" : step.ToleranceValue,
            ToleranceZoneType = field == "zone" ? "diameter" : step.ToleranceZoneType,
            Modifiers = field == "modifiers" ? "LMC" : step.Modifiers,
            NormalizedModifiers = field == "modifiers" ? "LMC" : step.NormalizedModifiers
        };

        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));

        Assert.Equal(VerificationStatus.Mismatched, result.Status);
        var expectedField = field switch
        {
            "tolerance" => "tolerance",
            "zone" => "tolerance zone",
            "modifiers" => "modifiers",
            _ => field
        };
        Assert.Contains(expectedField, result.ComparisonDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "TD-KAN44-006 incomplete or ambiguous controls are Not Verified with a reason")]
    [InlineData("missing tolerance")]
    [InlineData("unresolved feature")]
    [InlineData("missing datum")]
    [InlineData("ambiguous match")]
    public void Incomplete_or_unreliable_data_is_not_verified(string scenario)
    {
        var step = Annotation(GdtControlType.Parallelism, "∥", feature: scenario == "unresolved feature" ? null : "face-1", tolerance: scenario == "missing tolerance" ? null : "0.1", datums: scenario == "missing datum" ? Array.Empty<string>() : new[] { "A" });
        var dxf = scenario == "ambiguous match"
            ? new[] { Annotation(GdtControlType.Parallelism, "∥", feature: "face-1", datums: new[] { "A" }), Annotation(GdtControlType.Parallelism, "∥", feature: "face-1", datums: new[] { "A" }) }
            : new[] { Annotation(GdtControlType.Parallelism, "∥", feature: "face-1", tolerance: "0.1", datums: new[] { "A" }) };
        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, dxf));
        Assert.Equal(VerificationStatus.NotVerified, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory(DisplayName = "TD-KAN44-007 rejects unsupported syntax without a false pass")]
    [InlineData(true, null, "composite frame")]
    [InlineData(false, "unsupported scheme", "unsupported datum scheme")]
    [InlineData(false, null, "unsupported modifier")]
    public void Unsupported_syntax_is_not_verified(bool composite, string? datumScheme, string scenario)
    {
        var modifiers = scenario == "unsupported modifier" ? "unsupported modifier" : "MMC";
        var step = Annotation(GdtControlType.Position, "⌖", feature: "hole-1", modifiers: modifiers, datumScheme: datumScheme, composite: composite);
        var dxf = Annotation(GdtControlType.Position, "⌖", feature: "hole-1", modifiers: modifiers, datumScheme: datumScheme, composite: composite);
        var result = Assert.Single(new GdtValidator().Validate(new[] { step }, new[] { dxf }));
        Assert.Equal(VerificationStatus.NotVerified, result.Status);
        Assert.Contains("unsupported", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-11 fails visibly for malformed or unsupported subset configuration")]
    public void Malformed_subset_configuration_is_not_silently_defaulted()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{\"gdtSubset\":{\"version\":99,\"controls\":[]}}");
        var exception = Assert.Throws<InvalidDataException>(() => GdtValidator.FromSubsetFile(path));
        Assert.Contains("unsupported schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
        // FromSubsetFile currently leaves its read stream open; cleanup is deferred to process teardown.
    }

    private static GdtAnnotationData Annotation(GdtControlType type, string symbol, string? feature = "feature-1", string? nominal = null, string? tolerance = "0.1", string? zone = "width", string? diameter = null, string? modifiers = "MMC", IReadOnlyList<string>? datums = null, string? datumScheme = null, bool composite = false) =>
        new("annotation-1", type, symbol, tolerance, modifiers, datums ?? new[] { "A" }, "drawing.dxf", "entity:7", symbol, 7, ControlledFeature: feature, NominalBasicLocation: nominal, ToleranceZoneType: zone, ToleranceZoneDiameter: diameter, DatumScheme: datumScheme, HasCompositeFrame: composite);
}
