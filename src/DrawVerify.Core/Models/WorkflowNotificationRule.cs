using DrawVerify.Core.Enums;
namespace DrawVerify.Core.Models;
public sealed record WorkflowNotificationRule
{
    public WorkflowNotificationEvent Event { get; init; }
    public IReadOnlyList<string> RecipientRoles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<WorkflowNotificationChannel> Channels { get; init; } = new[] { WorkflowNotificationChannel.InApp };
    public string ContentTemplate { get; init; } = string.Empty;
    public bool ReminderEnabled { get; init; }
    public TimeSpan ReminderInterval { get; init; } = TimeSpan.FromDays(1);
    public bool Enabled { get; init; } = true;
}
