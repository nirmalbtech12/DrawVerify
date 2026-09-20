namespace DrawVerify.Core.Models;

public sealed record InAppNotification
{
    public Guid NotificationId { get; init; }
    public Guid SubmissionId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public Guid PackageId { get; init; }
    public int Revision { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
    public bool IsRead { get; init; }
    public string Target { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
}
