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
}
