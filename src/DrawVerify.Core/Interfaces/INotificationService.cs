using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Delivers user-facing notifications (in-app, e-mail, etc.).
/// </summary>
public interface INotificationService
{
    Task<IReadOnlyList<InAppNotification>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InAppNotification>>(Array.Empty<InAppNotification>());
    Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task NotifySubmissionAsync(ReviewSubmission submission, IReadOnlyList<WorkflowNotificationChannel> channels, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task NotifyMakerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task NotifyCheckerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task NotifyApproverAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task NotifyAdministratorAsync(Guid templateId, int templateVersion, Guid packageId, int revision, Guid placeholderId, string placeholderLabel, string reason, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
