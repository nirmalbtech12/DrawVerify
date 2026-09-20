using System.Collections.ObjectModel;
using System.Windows.Input;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class NotificationCenterViewModel : TaskScreenViewModel
{
    private readonly INotificationService _notifications;
    private readonly ISessionAuthenticationService _session;
    private readonly ICentralizedLogger? _logger;
    public ObservableCollection<InAppNotification> Notifications { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand ActivateCommand { get; }
    public string Error { get; private set; } = string.Empty;
    public event Action<Guid, int>? ReviewRequested;
    public NotificationCenterViewModel(INotificationService notifications, ISessionAuthenticationService session, ICentralizedLogger? logger = null)
    {
        _notifications = notifications; _session = session; _logger = logger; RefreshCommand = new Command(_ => _ = LoadAsync()); ActivateCommand = new Command(item => { if (item is InAppNotification notification) _ = ActivateAsync(notification); });
    }
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null) return;
        try { Notifications.Clear(); foreach (var notification in await _notifications.GetForUserAsync(_session.CurrentUser.UserId, cancellationToken).ConfigureAwait(true)) Notifications.Add(notification); Error = string.Empty; RaisePropertyChanged(nameof(Error)); }
        catch (Exception exception) { _logger?.Log(exception, "Notification center load", $"Unable to load notifications for userId={_session.CurrentUser.UserId}."); Error = "Notifications could not be loaded."; RaisePropertyChanged(nameof(Error)); }
    }
    public async Task ActivateAsync(InAppNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null) return;
        try { if (!notification.IsRead) { await _notifications.MarkReadAsync(notification.NotificationId, _session.CurrentUser.UserId, cancellationToken).ConfigureAwait(true); var index = Notifications.IndexOf(notification); if (index >= 0) Notifications[index] = notification with { IsRead = true }; } ReviewRequested?.Invoke(notification.PackageId, notification.Revision); }
        catch (Exception exception) { _logger?.Log(exception, "Notification access", $"Unable to mark notificationId={notification.NotificationId} as read."); Error = "The notification could not be opened."; RaisePropertyChanged(nameof(Error)); }
    }
    private sealed class Command(Action<object?> execute) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => execute(parameter); }
}
