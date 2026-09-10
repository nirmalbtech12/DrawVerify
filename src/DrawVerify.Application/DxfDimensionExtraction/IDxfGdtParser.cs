using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

public interface IDxfGdtParser
{
    Task<IReadOnlyList<GdtAnnotationData>> ParseAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default);
}
