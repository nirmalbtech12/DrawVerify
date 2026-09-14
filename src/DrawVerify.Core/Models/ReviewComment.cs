namespace DrawVerify.Core.Models;

public sealed record ReviewComment
{
    public Guid CommentId { get; init; } = Guid.NewGuid();
    public Guid PackageId { get; init; }
    public int Revision { get; init; }
    public string AuthorUserId { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string Text { get; init; } = string.Empty;
    public bool IsDraft { get; init; } = true;
    public string? ReferenceType { get; init; }
    public string? ReferenceId { get; init; }
    public string? Location { get; init; }
    public string? CorrectionResponse { get; init; }
    public Guid? PriorCommentId { get; init; }

    public void Validate()
    {
        if (PackageId == Guid.Empty) throw new ArgumentException("Package identifier is required.", nameof(PackageId));
        if (Revision < 1) throw new ArgumentOutOfRangeException(nameof(Revision));
        if (string.IsNullOrWhiteSpace(AuthorUserId)) throw new ArgumentException("Comment author is required.", nameof(AuthorUserId));
        if (string.IsNullOrWhiteSpace(Text)) throw new ArgumentException("Review comment cannot be blank.", nameof(Text));
        if (!string.IsNullOrWhiteSpace(ReferenceType) && !new[] { "drawing", "model", "dimension", "gdt" }.Contains(ReferenceType.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("The issue reference type is not supported.", nameof(ReferenceType));
        if (!string.IsNullOrWhiteSpace(ReferenceType) && string.IsNullOrWhiteSpace(ReferenceId) && string.IsNullOrWhiteSpace(Location))
            throw new ArgumentException("An issue reference requires an identifier or location.", nameof(ReferenceId));
    }
}
