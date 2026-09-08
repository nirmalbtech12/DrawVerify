namespace DrawVerify.Application.DxfDimensionExtraction;

public sealed class DxfDimensionExtractionRequest
{
    public DxfDimensionExtractionRequest(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("A document id is required.", nameof(documentId));
        DocumentId = documentId;
        CancellationToken = cancellationToken;
    }

    public Guid DocumentId { get; }
    public CancellationToken CancellationToken { get; }
}
