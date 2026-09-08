using System.Text;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DxfDimensionParserTests
{
    [Fact(DisplayName = "TD-KAN25-APP-001 parses standard linear, angular, and radial dimensions")]
    public async Task Standard_dimension_entities_are_typed_and_linked()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var parser = new DxfDimensionParser();
        await using var stream = AsStream(
            Dimension(0, "10.0", "horizontal"),
            Dimension(1, "20.0", "vertical"),
            Dimension(0, "30.0", "aligned"),
            Dimension(2, "45.0", "angle"),
            Dimension(3, "5.0", "R5"),
            Dimension(4, "12.0", "Ø12"));

        var result = await parser.ParseAsync(stream, documentId, versionId);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Warnings);
        Assert.Equal(6, result.Dimensions.Count);
        Assert.Equal(new[]
        {
            DimensionType.Linear,
            DimensionType.Linear,
            DimensionType.Linear,
            DimensionType.Angular,
            DimensionType.Radial,
            DimensionType.Radial
        }, result.Dimensions.Select(dimension => dimension.DimensionType));
        Assert.Equal(new[] { 10.0, 20.0, 30.0, 45.0, 5.0, 12.0 }, result.Dimensions.Select(dimension => dimension.NominalValue));
        Assert.Equal(new[] { "horizontal", "vertical", "aligned", "angle", "R5", "Ø12" }, result.Dimensions.Select(dimension => dimension.DisplayText));
        Assert.Equal(RadialDimensionKind.Diameter, result.Dimensions[4].RadialKind);
        Assert.Equal(RadialDimensionKind.Radius, result.Dimensions[5].RadialKind);
        Assert.All(result.Dimensions, dimension =>
        {
            Assert.Equal(documentId, dimension.DocumentId);
            Assert.Equal(versionId, dimension.DocumentVersionId);
        });
        var ordinals = result.Dimensions.Select(dimension => dimension.SourceEntityOrdinal).ToArray();
        Assert.Equal(ordinals.Length, ordinals.Distinct().Count());
        Assert.Equal(ordinals.OrderBy(ordinal => ordinal), ordinals);
    }

    [Fact(DisplayName = "TD-03 preserves unknown radial subtype for legacy records")]
    public async Task Legacy_radial_record_defaults_to_unknown_subtype()
    {
        var result = await new DxfDimensionParser().ParseAsync(AsStream(Dimension(5, "7.0", "radial")), Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(RadialDimensionKind.Unknown, Assert.Single(result.Dimensions).RadialKind);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 parses explicitly encoded tolerance forms")]
    public async Task Explicit_tolerance_forms_populate_lower_and_upper_values()
    {
        var parser = new DxfDimensionParser();
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using var stream = AsStream(
            Dimension(0, "10.0", "10.0 %%p0.1"),
            Dimension(0, "10.0", "10.0 +0.2/-0.1"),
            Dimension(0, "10.0", "10.0 9.9-10.2"),
            Dimension(0, "10.0", "10.0", "47\n-0.15\n48\n0.25\n"));

        var result = await parser.ParseAsync(stream, documentId, versionId);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Dimensions.Count);
        Assert.Equal((-0.1, 0.1), (result.Dimensions[0].LowerTolerance, result.Dimensions[0].UpperTolerance));
        Assert.Equal((-0.1, 0.2), (result.Dimensions[1].LowerTolerance, result.Dimensions[1].UpperTolerance));
        Assert.Equal((-0.1, 0.2), (result.Dimensions[2].LowerTolerance, result.Dimensions[2].UpperTolerance));
        Assert.Equal((-0.15, 0.25), (result.Dimensions[3].LowerTolerance, result.Dimensions[3].UpperTolerance));
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 leaves tolerance fields null when absent")]
    public async Task Dimensions_without_explicit_tolerances_have_null_tolerance_fields()
    {
        var parser = new DxfDimensionParser();
        await using var stream = AsStream(Dimension(0, "4.25", "4.25"));

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        var dimension = Assert.Single(result.Dimensions);
        Assert.Null(dimension.LowerTolerance);
        Assert.Null(dimension.UpperTolerance);
        Assert.Equal("4.25", dimension.DisplayText);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 accepts a valid empty ENTITIES section")]
    public async Task Empty_entities_section_is_a_success_with_no_dimensions()
    {
        var parser = new DxfDimensionParser();
        await using var stream = AsStream(string.Empty);

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Dimensions);
        Assert.Empty(result.Warnings);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 skips unsupported custom dimension styles safely")]
    public async Task Unsupported_dimension_type_is_skipped_without_crashing()
    {
        var parser = new DxfDimensionParser();
        await using var stream = AsStream(Dimension(8, "99.0", "custom"));

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Dimensions);
        Assert.Contains(result.Warnings, warning => warning.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 keeps valid dimensions after a malformed entity")]
    public async Task Recoverable_malformed_entity_is_reported_as_partial()
    {
        var parser = new DxfDimensionParser();
        await using var stream = AsStream(DimensionWithoutNominal(0, "bad"), Dimension(3, "8.0", "R8"));

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.True(result.IsPartial);
        Assert.Single(result.Dimensions);
        Assert.Equal(DimensionType.Radial, result.Dimensions[0].DimensionType);
        Assert.Contains(result.Warnings, warning => warning.Contains("Skipped malformed DIMENSION entity", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 classifies malformed-only DXF content as failure")]
    public async Task Malformed_only_entities_section_is_fatal_and_has_no_dimensions()
    {
        var parser = new DxfDimensionParser();
        await using var stream = AsStream(DimensionWithoutNominal(0, "bad"));

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Warnings);
        Assert.Empty(result.Dimensions);
        Assert.Contains("readable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 rejects unreadable group-code records without recoverable dimensions")]
    public async Task Unreadable_group_code_is_fatal_without_recoverable_dimensions()
    {
        var parser = new DxfDimensionParser();
        await using var stream = RawStream("0\nSECTION\n2\nENTITIES\nnot-a-group-code\nignored\n0\nENDSEC\n0\nEOF\n");

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Empty(result.Dimensions);
        Assert.Contains(result.Warnings, warning => warning.Contains("Unreadable DXF group code", StringComparison.Ordinal));
        Assert.Contains("readable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 rejects incomplete group-code records without recoverable dimensions")]
    public async Task Incomplete_group_code_pair_is_fatal_without_recoverable_dimensions()
    {
        var parser = new DxfDimensionParser();
        await using var stream = RawStream("0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n");

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Empty(result.Dimensions);
        Assert.Contains(result.Warnings, warning => warning.Contains("middle of a group-code record", StringComparison.Ordinal));
        Assert.Contains("readable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 rejects a DXF without an ENTITIES section")]
    public async Task Missing_entities_section_is_fatal_and_has_no_dimensions()
    {
        var parser = new DxfDimensionParser();
        await using var stream = RawStream("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nEOF\n");

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Empty(result.Dimensions);
        Assert.Contains("ENTITIES", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN25-APP-001 reads DXF sequentially with a bounded parser buffer")]
    public async Task Parser_uses_bounded_sequential_reads_and_leaves_caller_stream_open()
    {
        var parser = new DxfDimensionParser();
        var stream = new TrackingReadStream(Encoding.UTF8.GetBytes(Dxf(Dimension(0, "12.0", "12"))));

        var result = await parser.ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.True(stream.ReadCalls > 0);
        Assert.InRange(stream.MaximumRequestedRead, 1, 4096);
        Assert.False(stream.IsDisposed);
        await stream.DisposeAsync();
    }

    private static TrackingReadStream AsStream(params string[] entities) =>
        new(Encoding.UTF8.GetBytes(Dxf(entities)));

    private static TrackingReadStream RawStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    private static string Dxf(params string[] entities)
    {
        var builder = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
        foreach (var entity in entities)
            builder.Append(entity);
        builder.Append("0\nENDSEC\n0\nEOF\n");
        return builder.ToString();
    }

    private static string Dimension(int flags, string nominal, string? displayText, string extraPairs = "") =>
        $"0\nDIMENSION\n70\n{flags}\n42\n{nominal}\n" +
        (displayText is null ? string.Empty : $"1\n{displayText}\n") + extraPairs;

    private static string DimensionWithoutNominal(int flags, string displayText) =>
        $"0\nDIMENSION\n70\n{flags}\n1\n{displayText}\n";

    private sealed class TrackingReadStream(byte[] content) : MemoryStream(content)
    {
        public int ReadCalls { get; private set; }
        public int MaximumRequestedRead { get; private set; }
        public bool IsDisposed { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            MaximumRequestedRead = Math.Max(MaximumRequestedRead, buffer.Length);
            return base.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
