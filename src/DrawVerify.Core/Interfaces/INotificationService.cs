using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Delivers user-facing notifications (in-app, e-mail, etc.).
/// </summary>
public interface INotificationService
{
    Task NotifyMakerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    Task NotifyCheckerAsync(Guid packageId, int revision, ReviewDecision decision, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
