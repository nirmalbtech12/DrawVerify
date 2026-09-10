using System.Text;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class DxfGdtParserTests
{
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
    }

    [Fact(DisplayName = "TD-04 honors cancellation")]
    public async Task Cancellation_is_visible()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nTEXT\n1\nPOSITION 0.1\n"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DxfGdtParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid(), cancellation.Token));
    }
}
