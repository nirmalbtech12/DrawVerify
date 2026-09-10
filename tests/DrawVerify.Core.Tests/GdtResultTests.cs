using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class GdtResultTests
{
    [Fact(DisplayName = "TD-KAN44-001 preserves complete GD&T evidence and statuses")]
    public void Complete_result_contract_preserves_all_supported_evidence()
    {
        var id = Guid.NewGuid();
        var result = new GdtResult
        {
            GdtResultId = id,
            VerificationSessionId = Guid.NewGuid(),
            Identifier = "pos-1",
            ControlType = GdtControlType.Position,
            Symbol = "⌖",
            SourceDocument = "pair-1.step",
            SourceLocation = "pmi:7",
            ControlledFeature = "hole-7",
            NominalBasicLocation = "10,20",
            ToleranceValue = "0.1",
            ToleranceZoneType = "diameter",
            ToleranceZoneDiameter = "0.2",
            NormalizedModifiers = "MMC",
            DatumReferences = new[] { "A", "B", "C" },
            RuleVersion = "1.0.0",
            ComparisonCriteria = "feature/location/tolerance/datum-order",
            ComparisonDetails = "All applicable GD&T fields agree.",
            SourcePairId = "pair-1",
            Diagnostics = new[] { "mapped from STEP PMI" },
            Status = VerificationStatus.Passed
        };

        Assert.Equal(id, result.GdtResultId);
        Assert.Equal(new[] { "A", "B", "C" }, result.DatumReferences);
        Assert.Equal("1.0.0", result.RuleVersion);
        Assert.Equal("diameter", result.ToleranceZoneType);
        Assert.Equal("All applicable GD&T fields agree.", result.ComparisonDetails);
        Assert.Equal(4, new[] { VerificationStatus.Passed, VerificationStatus.Failed, VerificationStatus.Mismatched, VerificationStatus.NotVerified }.Length);
    }

    [Fact(DisplayName = "TD-KAN44-001 keeps flatness datum optional and review status immutable")]
    public void Flatness_without_datum_is_valid_contract_and_review_does_not_change_status()
    {
        var result = new GdtResult
        {
            Identifier = "flat-1", ControlType = GdtControlType.Flatness, SourceDocument = "pair.dxf",
            ToleranceValue = "0.05", ControlledFeature = "surface-1", DatumReferences = Array.Empty<string>(),
            RuleVersion = GdtResult.CurrentRuleVersion, Status = VerificationStatus.Mismatched
        };

        var reviewed = result.WithReview("Check surface association", true);

        Assert.Empty(result.DatumReferences);
        Assert.Equal(VerificationStatus.Mismatched, reviewed.Status);
        Assert.Equal("Check surface association", reviewed.ReviewComment);
        Assert.True(reviewed.RequiresEngineeringReview);
        Assert.Equal(result.RuleVersion, reviewed.RuleVersion);
    }
    [Fact(DisplayName = "TD-01 keeps unsupported results separate from dimension statuses")]
    public void Unsupported_result_has_exact_reason_and_separate_count()
    {
        var unsupported = GdtResult.Unsupported("gdt-1", GdtControlType.Unknown, "◇", "drawing.dxf", "entity:4");
        var dimensions = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Matched },
            new DimensionMatch { Status = DimensionMatchStatus.Mismatched },
            new DimensionMatch { Status = DimensionMatchStatus.MissingDimension },
            new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension }
        };

        var summary = ComparisonSummary.From(dimensions, new[] { unsupported });

        Assert.Equal(VerificationStatus.CannotValidate, unsupported.Status);
        Assert.Equal(GdtResult.UnsupportedReason, unsupported.Reason);
        Assert.Equal(1, summary.CannotValidateGdtControls);
        Assert.Equal(1, summary.MatchedDimensions);
        Assert.Equal(1, summary.MismatchedDimensions);
        Assert.Equal(1, summary.MissingDimensions);
        // The legacy summary intentionally groups MissingDimension and ExtraDimension
        // under ExtraDimensions; NotVerifiedGdtControls remains independently counted.
        Assert.Equal(2, summary.ExtraDimensions);
    }

    [Fact(DisplayName = "TD-CORE-001 preserves Cannot Validate during review")]
    public void Review_metadata_does_not_change_status()
    {
        var result = GdtResult.Unsupported("gdt-2", GdtControlType.Flatness, "⏥", "model.step", "pmi:2");
        var reviewed = result.WithReview("Needs engineering review", true);

        Assert.Equal(VerificationStatus.CannotValidate, reviewed.Status);
        Assert.Equal("Needs engineering review", reviewed.ReviewComment);
        Assert.True(reviewed.RequiresEngineeringReview);
        Assert.Equal(result.GdtResultId, reviewed.GdtResultId);
    }

    [Fact(DisplayName = "TD-01 rejects invalid unsupported result input")]
    public void Unsupported_result_requires_identifier_and_source()
    {
        Assert.Throws<ArgumentException>(() => GdtResult.Unsupported("", sourceDocument: "drawing.dxf"));
        Assert.Throws<ArgumentException>(() => GdtResult.Unsupported("gdt-3"));
        Assert.Throws<ArgumentException>(() => GdtResult.Unsupported("gdt-4", (GdtControlType)99, sourceDocument: "drawing.dxf"));
    }

    [Fact(DisplayName = "TD-CORE-001 counts Cannot Validate GD&T separately from dimension outcomes")]
    public void Not_verified_count_does_not_change_dimension_totals()
    {
        var dimensions = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 1 },
            new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DxfDimensionReference = 2 }
        };
        var summary = ComparisonSummary.From(dimensions, new[] { GdtResult.Unsupported("gdt-count", sourceDocument: "drawing.dxf") });

        Assert.Equal(2, summary.TotalDimensions);
        Assert.Equal(1, summary.MatchedDimensions);
        Assert.Equal(1, summary.MismatchedDimensions);
        Assert.Equal(1, summary.CannotValidateGdtControls);
    }

    [Fact(DisplayName = "TD-CORE-001 preserves canonical Cannot Validate reasons, metadata, and status exclusion")]
    public void Cannot_validate_result_preserves_metadata_and_is_not_a_validation_classification()
    {
        var result = new GdtResult
        {
            Identifier = "gdt-gap", ControlType = GdtControlType.Unknown, Symbol = "◇", SourceDocument = "drawing.dxf", SourceLocation = "entity:42",
            SourceDocumentVersion = "v3", EntityHandle = "42", RawText = "◇ ?", Diagnostics = new[] { "unrecognized modifier" },
            ReviewDisposition = "Engineering review", Status = VerificationStatus.CannotValidate, Reason = GdtResult.UnsupportedReason
        };

        Assert.Equal(VerificationStatus.CannotValidate, result.Status);
        Assert.Equal("Unsupported GD&T control type", result.Reason);
        Assert.Equal("drawing.dxf", result.SourceDocument);
        Assert.Equal("entity:42", result.SourceLocation);
        Assert.Equal("v3", result.SourceDocumentVersion);
        Assert.Equal(new[] { "unrecognized modifier" }, result.Diagnostics);
        Assert.DoesNotContain(result.Status, new[] { VerificationStatus.Passed, VerificationStatus.Failed, VerificationStatus.Mismatched });
        Assert.Equal("Engineering review", result.ReviewDisposition);
    }

    [Fact(DisplayName = "TD-CORE-001 keeps review metadata and Cannot Validate status immutable")]
    public void Cannot_validate_review_copy_keeps_status_and_review_data()
    {
        var result = GdtResult.Unsupported("gdt-review", GdtControlType.Unknown, sourceDocument: "model.step", sourceLocation: "pmi:9");
        var reviewed = result.WithReview("Needs syntax review", true, DateTimeOffset.UtcNow);

        Assert.Equal(VerificationStatus.CannotValidate, reviewed.Status);
        Assert.Equal(GdtResult.UnsupportedReason, reviewed.Reason);
        Assert.Equal("Needs syntax review", reviewed.ReviewComment);
        Assert.True(reviewed.RequiresEngineeringReview);
        Assert.Equal(result.GdtResultId, reviewed.GdtResultId);
    }
}
