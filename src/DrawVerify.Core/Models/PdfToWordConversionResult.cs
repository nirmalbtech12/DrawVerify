namespace DrawVerify.Core.Models;

public sealed record PdfToWordConversionResult
{
    public byte[] Content { get; init; } = [];
    public int PageCount { get; init; }
    public bool HasImages { get; init; }
}
