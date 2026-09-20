using System.Collections.ObjectModel;
using System.ComponentModel;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class NotificationSettingsViewModel : INotifyPropertyChanged
{
    private bool _submissionNotificationsEnabled = true;
    private bool _approvalNotificationsEnabled = true;
    public bool SubmissionNotificationsEnabled { get => _submissionNotificationsEnabled; set { if (_submissionNotificationsEnabled == value) return; _submissionNotificationsEnabled = value; PropertyChanged?.Invoke(this, new(nameof(SubmissionNotificationsEnabled))); } }
    public bool ApprovalNotificationsEnabled { get => _approvalNotificationsEnabled; set { if (_approvalNotificationsEnabled == value) return; _approvalNotificationsEnabled = value; PropertyChanged?.Invoke(this, new(nameof(ApprovalNotificationsEnabled))); } }
    public ObservableCollection<WorkflowNotificationChannel> Channels { get; } = new() { WorkflowNotificationChannel.InApp };
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkflowNotificationRule CreateSubmissionRule(IEnumerable<string> recipientRoles)
    {
        var roles = recipientRoles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roles.Length == 0) throw new InvalidOperationException("At least one notification recipient role is required.");
        if (Channels.Count == 0) throw new InvalidOperationException("At least one notification channel is required.");
        return new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Submission, RecipientRoles = roles, Channels = Channels.Distinct().ToArray(), ContentTemplate = "submitted for review", Enabled = SubmissionNotificationsEnabled };
    }

    public WorkflowNotificationRule CreateApprovalRule(IEnumerable<string> recipientRoles)
    {
        var roles = recipientRoles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roles.Length == 0) throw new InvalidOperationException("At least one notification recipient role is required.");
        if (Channels.Count == 0) throw new InvalidOperationException("At least one notification channel is required.");
        if (Channels.Any(channel => channel != WorkflowNotificationChannel.InApp)) throw new NotSupportedException("Only the configured in-app notification channel is available.");
        return new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Approval, RecipientRoles = roles, Channels = Channels.Distinct().ToArray(), ContentTemplate = "approved by Checker - ready for final approval", Enabled = ApprovalNotificationsEnabled };
    }
}
