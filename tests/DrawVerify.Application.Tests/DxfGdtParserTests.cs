using System.Text;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Application.Verification;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DxfGdtParserTests
{
    [Fact(DisplayName = "TD-KAN44-006 preserves parser diagnostics for incomplete extraction")]
    public async Task Incomplete_frame_is_retained_with_a_specific_diagnostic()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nMTEXT\n1\nPARALLELISM\n"));
        var result = await new DxfGdtParser().ParseDetailedAsync(stream, Guid.NewGuid(), Guid.NewGuid());
        var annotation = Assert.Single(result.Annotations);
        Assert.Equal(GdtControlType.Parallelism, annotation.ControlType);
        Assert.NotEmpty(annotation.Diagnostics!);
        Assert.Contains(annotation.Diagnostics!, value => value.Contains("tolerance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "TD-KAN44-007 keeps unsupported DXF syntax from being interpreted as a pass")]
    public async Task Unsupported_frame_text_is_preserved_for_not_verified_handling()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nTEXT\n1\nPOSITION 0.1 COMPOSITE UNSUPPORTED\n"));
        var result = await new DxfGdtParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());
        var annotation = Assert.Single(result);
        Assert.Equal(GdtControlType.Position, annotation.ControlType);
        Assert.Contains("UNSUPPORTED", annotation.RawText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN45-REVIEW-01 classifies unsupported DXF modifiers through the validator")]
    public async Task Unsupported_modifier_is_cannot_validate_with_canonical_reason()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nTEXT\n1\nPOSITION 0.1 COMPOSITE UNSUPPORTED\n"));

        var annotation = Assert.Single(await new DxfGdtParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid()));
        var result = Assert.Single(new GdtValidator().Validate(new[] { annotation }));

        Assert.Equal(VerificationStatus.CannotValidate, result.Status);
        Assert.Equal(GdtResult.UnsupportedSyntaxReason, result.Reason);
        Assert.True(annotation.HasCompositeFrame);
        Assert.True(annotation.UnsupportedSyntax);
        Assert.Contains(annotation.Diagnostics!, diagnostic => diagnostic.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }

    [Theory(DisplayName = "TD-KAN45-REVIEW-01 classifies every recognized unsupported DXF modifier")]
    [InlineData("MMC")]
    [InlineData("RFS")]
    [InlineData("LMC")]
    [InlineData("PROJECTED")]
    [InlineData("TANGENT")]
    [InlineData("FREE STATE")]
    public async Task Every_recognized_unsupported_modifier_is_cannot_validate(string modifier)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"0\nTEXT\n1\nPOSITION 0.1 {modifier}\n"));

        var annotation = Assert.Single(await new DxfGdtParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid()));
        var result = Assert.Single(new GdtValidator().Validate(new[] { annotation }));

        Assert.Equal(VerificationStatus.CannotValidate, result.Status);
        Assert.Equal(GdtResult.UnsupportedSyntaxReason, result.Reason);
        Assert.True(annotation.UnsupportedSyntax);
    }
    [Fact(DisplayName = "TD-04 parses supported DXF feature-control frames and preserves source data")]
    public async Task Parses_supported_frame_fields_and_leaves_stream_open()
    {
        var document = Guid.NewGuid();
        var version = Guid.NewGuid();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nTEXT\n10\n12.5\n20\n7.5\n1\nPOSITION 0.1|A B\n0\nENDSEC\n"));

        var result = Assert.Single(await new DxfGdtParser().ParseAsync(stream, document, version));

        Assert.Equal(GdtControlType.Position, result.ControlType);
        Assert.Equal("POSITION", result.Symbol);
        Assert.Equal("0.1", result.ToleranceValue);
        Assert.Equal(new[] { "A", "B" }, result.DatumReferences);
        Assert.Equal("12.5,7.5", result.SourceLocation);
        Assert.Equal($"DXF:{document:N}", result.SourceDocument);
        Assert.True(stream.CanRead);
    }

    [Fact(DisplayName = "TD-04 retains malformed or unknown detected annotation text")]
    public async Task Retains_unknown_symbol_as_unknown_annotation()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nMTEXT\n1\n◇ 0.2\n"));

        var result = Assert.Single(await new DxfGdtParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(GdtControlType.Unknown, result.ControlType);
        Assert.Equal("◇ 0.2", result.RawText);
        Assert.StartsWith("entity:", result.SourceLocation);
        Assert.Equal(result.SourceOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture), result.EntityHandle);
    }

    [Fact(DisplayName = "TD-04 exposes structured DXF diagnostics through the production parser contract")]
    public async Task Detailed_parse_preserves_diagnostics()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nMTEXT\n1\nPOSITION\n"));

        IDxfGdtParser parser = new DxfGdtParser();
        var result = await parser.ParseDetailedAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("tolerance", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(Assert.Single(result.Annotations).Diagnostics!);
    }

    [Fact(DisplayName = "TD-04 honors cancellation")]
    public async Task Cancellation_is_visible()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nTEXT\n1\nPOSITION 0.1\n"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DxfGdtParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid(), cancellation.Token));
    }

    [Fact(DisplayName = "TD-05 distinguishes unsupported STEP capability from operational extraction failure")]
    public void Extraction_failure_categories_remain_explicit()
    {
        var unsupported = GdtAnnotationExtractionResult.Unsupported("STEP annotations are not supported by this worker.");
        var failure = GdtAnnotationExtractionResult.Failure("WorkerTimeout", "STEP annotation worker timed out.");

        Assert.Equal("CapabilityUnavailable", unsupported.ErrorCategory);
        Assert.Equal("WorkerTimeout", failure.ErrorCategory);
        Assert.NotEqual(unsupported.ErrorCategory, failure.ErrorCategory);
    }
}
