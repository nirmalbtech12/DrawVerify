using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IDxfDrawingGenerationService
{
    bool CanGenerate { get; }
    Task<DxfDrawingGenerationResult> GenerateAsync(Guid stepDocumentId, CancellationToken cancellationToken = default, IProgress<DxfDrawingGenerationProgress>? progress = null);
    Task<Stream> DownloadAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
}

public sealed record DxfDrawingGenerationProgress(string Stage, double Percent, string Message);

public sealed record DxfDrawingGenerationResult(
    bool Succeeded,
    string Message,
    Document? Document = null,
    IReadOnlyList<string>? Diagnostics = null,
    Guid? VersionId = null)
{
    public IReadOnlyList<string> Diagnostics { get; init; } = Diagnostics ?? Array.Empty<string>();
    public int ExtractedDimensionCount { get; init; }
}
