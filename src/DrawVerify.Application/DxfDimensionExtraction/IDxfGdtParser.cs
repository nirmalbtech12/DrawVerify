using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

public interface IDxfGdtParser
{
    async Task<IReadOnlyList<GdtAnnotationData>> ParseAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
        => (await ParseDetailedAsync(content, documentId, documentVersionId, cancellationToken).ConfigureAwait(false)).Annotations;

    Task<DxfGdtExtractionResult> ParseDetailedAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default);
}
