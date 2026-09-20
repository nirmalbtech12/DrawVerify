using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class InternalSharedLinkViewModel : INotifyPropertyChanged
{
    private readonly IInternalSharedLinkService _service;
    private readonly ICentralizedLogger _logger;
    private InternalSharedLinkDetails? _selectedLink;
    private string _error = string.Empty;
    public ObservableCollection<InternalSharedLinkDetails> Links { get; } = new();
    public InternalSharedLinkDetails? SelectedLink { get => _selectedLink; private set { _selectedLink = value; PropertyChanged?.Invoke(this, new(nameof(SelectedLink))); PropertyChanged?.Invoke(this, new(nameof(CanCopyLink))); } }
    public string Error { get => _error; private set { _error = value; PropertyChanged?.Invoke(this, new(nameof(Error))); } }
    public SharedLinkAccessLevel AccessLevel { get; set; }
    public bool ActivateImmediately { get; set; } = true;
    public DateTime? ExpiresUtc { get; set; }
    public bool CanCopyLink => SelectedLink?.Status == SharedLinkStatus.Active;
    public ICommand GenerateCommand { get; }
    public ICommand CopyLinkCommand { get; }
    public ICommand RevokeCommand { get; }
    public ICommand RegenerateCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public InternalSharedLinkViewModel(IInternalSharedLinkService service, ICentralizedLogger? logger = null) { _service = service; _logger = logger ?? new Infrastructure.Logging.CentralizedLogger(); GenerateCommand = new Command(_ => GenerateAsync()); CopyLinkCommand = new Command(_ => CopyAsync(), _ => CanCopyLink); RevokeCommand = new Command(_ => RevokeAsync(), _ => SelectedLink is not null); RegenerateCommand = new Command(_ => RegenerateAsync(), _ => SelectedLink is not null); }
    public async Task LoadAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default) { try { Links.Clear(); foreach (var link in await _service.ListAsync(documentId, versionId, cancellationToken)) Links.Add(link); SelectedLink = Links.FirstOrDefault(); Error = string.Empty; } catch (Exception ex) { _logger.Log(ex, "Shared-link details", $"Unable to load shared links for document {documentId}."); Error = "Unable to load shared-link details."; } }
    public Task GenerateAsync() => GenerateAsync(Guid.Empty, Guid.Empty);
    public async Task GenerateAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) { try { var link = await _service.GenerateAsync(documentId, versionId, new InternalSharedLinkConfiguration { AccessLevel = AccessLevel, IsActive = ActivateImmediately, ExpiresUtc = ExpiresUtc }); Links.Add(link); SelectedLink = link; Error = string.Empty; } catch (Exception ex) { _logger.Log(ex, "Shared-link generation", "Unable to generate an internal shared link."); Error = ex.Message; } }
    public async Task CopyAsync() { if (!CanCopyLink || SelectedLink is null) return; try { Clipboard.SetText(SelectedLink.FullUrl); Error = "Link copied to the clipboard."; } catch (Exception ex) { _logger.Log(ex, "Shared-link clipboard", $"Unable to copy link {SelectedLink.Link.LinkId}; raw URL was not logged."); Error = "The link could not be copied to the clipboard."; } }
    public async Task RevokeAsync() { if (SelectedLink is null) return; try { await _service.RevokeAsync(SelectedLink.Link.LinkId); SelectedLink = await _service.GetDetailsAsync(SelectedLink.Link.LinkId); Error = string.Empty; } catch (Exception ex) { _logger.Log(ex, "Shared-link revocation", $"Unable to revoke link {SelectedLink.Link.LinkId}."); Error = ex.Message; } }
    public async Task RegenerateAsync() { if (SelectedLink is null) return; try { var replacement = await _service.RegenerateAsync(SelectedLink.Link.LinkId); Links.Add(replacement); SelectedLink = replacement; Error = string.Empty; } catch (Exception ex) { _logger.Log(ex, "Shared-link regeneration", $"Unable to regenerate link {SelectedLink.Link.LinkId}."); Error = ex.Message; } }
    private sealed class Command(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public async void Execute(object? parameter) => await execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
