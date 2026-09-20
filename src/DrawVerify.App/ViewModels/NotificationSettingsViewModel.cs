using System.Collections.ObjectModel;
using System.ComponentModel;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class NotificationSettingsViewModel : INotifyPropertyChanged
{
    private bool _submissionNotificationsEnabled = true;
    public bool SubmissionNotificationsEnabled { get => _submissionNotificationsEnabled; set { if (_submissionNotificationsEnabled == value) return; _submissionNotificationsEnabled = value; PropertyChanged?.Invoke(this, new(nameof(SubmissionNotificationsEnabled))); } }
    public ObservableCollection<WorkflowNotificationChannel> Channels { get; } = new() { WorkflowNotificationChannel.InApp };
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkflowNotificationRule CreateSubmissionRule(IEnumerable<string> recipientRoles)
    {
        var roles = recipientRoles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roles.Length == 0) throw new InvalidOperationException("At least one notification recipient role is required.");
        if (Channels.Count == 0) throw new InvalidOperationException("At least one notification channel is required.");
        return new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Submission, RecipientRoles = roles, Channels = Channels.Distinct().ToArray(), ContentTemplate = "submitted for review", Enabled = SubmissionNotificationsEnabled };
    }
}
