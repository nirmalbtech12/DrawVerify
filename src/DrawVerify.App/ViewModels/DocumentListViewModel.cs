namespace DrawVerify.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DrawVerify.Core.Models;
using DrawVerify.Core.Interfaces;

public sealed class DocumentListViewModel : INotifyPropertyChanged
{
    private readonly IViewerAccessService? _sharingService;
    public ObservableCollection<DocumentListItem> Documents { get; } = new();
    public string SharingStatusMessage { get; private set; } = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentListViewModel() { }
    public DocumentListViewModel(IViewerAccessService sharingService) => _sharingService = sharingService ?? throw new ArgumentNullException(nameof(sharingService));

    public Task LoadAsync(IEnumerable<DocumentListItem> rows, CancellationToken cancellationToken = default)
        => _sharingService is null
            ? throw new InvalidOperationException("Viewer sharing status service is required to load the document list.")
            : LoadAsync(rows, _sharingService, cancellationToken);

    public async Task LoadAsync(IEnumerable<DocumentListItem> rows, IViewerAccessService service, CancellationToken cancellationToken = default)
    {
        Documents.Clear();
        foreach (var row in rows) Documents.Add(row);
        SharingStatusMessage = string.Empty;
        Changed(nameof(Documents));
        await RefreshSharingStatusAsync(service, cancellationToken).ConfigureAwait(true);
    }
    public void SetSharingStatus(Guid documentId, Guid versionId, int count)
    {
        var row = Documents.FirstOrDefault(x => x.DocumentId == documentId && x.VersionId == versionId);
        if (row is null) return;
        row.ViewerShareCount = count; row.ViewerSharingStatus = count == 0 ? "Not shared" : $"Shared ({count})"; row.SharingStatusAvailable = true; Changed(nameof(Documents));
    }
    public void SetSharingStatusUnavailable(Guid documentId, Guid versionId, Exception exception)
    {
        var row = Documents.FirstOrDefault(x => x.DocumentId == documentId && x.VersionId == versionId);
        if (row is null) return;
        row.ViewerSharingStatus = "Unavailable"; row.SharingStatusAvailable = false; Changed(nameof(Documents));
    }
    public async Task RefreshSharingStatusAsync(IViewerAccessService service, CancellationToken cancellationToken = default)
    {
        foreach (var row in Documents)
        {
            try { SetSharingStatus(row.DocumentId, row.VersionId, await service.GetActiveShareCountAsync(row.DocumentId, row.VersionId, cancellationToken).ConfigureAwait(true)); }
            catch (Exception exception) { SetSharingStatusUnavailable(row.DocumentId, row.VersionId, exception); }
        }
        SharingStatusMessage = Documents.Any(x => !x.SharingStatusAvailable) ? "Viewer sharing status is unavailable for one or more documents." : string.Empty;
        Changed(nameof(SharingStatusMessage));
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class DocumentListItem
{
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public string DocumentName { get; init; } = string.Empty;
    public int ViewerShareCount { get; set; }
    public string ViewerSharingStatus { get; set; } = "Unavailable";
    public bool SharingStatusAvailable { get; set; }
}
