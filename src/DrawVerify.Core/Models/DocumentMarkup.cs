namespace DrawVerify.Core.Models;

public sealed record DocumentMarkup
{
    public Guid MarkupId { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public Guid DocumentVersionId { get; init; }
    public Guid PackageId { get; init; }
    public int Revision { get; init; }
    public string AuthorUserId { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string MarkupType { get; init; } = "annotation";
    public string DataJson { get; init; } = string.Empty;
    public int PageNumber { get; init; } = 1;
    public string? Location { get; init; }

    public void Validate()
    {
        if (DocumentId == Guid.Empty || DocumentVersionId == Guid.Empty) throw new ArgumentException("Document and document version identifiers are required.");
        if (PackageId == Guid.Empty || Revision < 1) throw new ArgumentException("A valid review package and revision are required.");
        if (string.IsNullOrWhiteSpace(AuthorUserId)) throw new ArgumentException("Markup author is required.", nameof(AuthorUserId));
        if (string.IsNullOrWhiteSpace(MarkupType) || string.IsNullOrWhiteSpace(DataJson)) throw new ArgumentException("Markup type and annotation data are required.");
        if (PageNumber < 1) throw new ArgumentOutOfRangeException(nameof(PageNumber));
    }
}
