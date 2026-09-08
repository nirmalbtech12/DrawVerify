namespace DrawVerify.Application.DxfDimensionExtraction;

public interface IDxfDimensionParser
{
    Task<DxfDimensionExtractionResult> ParseAsync(
        Stream content,
        Guid documentId,
        Guid documentVersionId,
        CancellationToken cancellationToken = default);
}
