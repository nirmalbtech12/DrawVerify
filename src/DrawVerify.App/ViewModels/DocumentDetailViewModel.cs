namespace DrawVerify.App.ViewModels;

public sealed class DocumentDetailViewModel
{
    public DocumentDetailViewModel() { }
    public DocumentDetailViewModel(DrawVerify.Core.Models.Document document) => Load(document);
    public DrawVerify.Core.Models.Document? Document { get; private set; }
    public void Load(DrawVerify.Core.Models.Document document) => Document = document ?? throw new ArgumentNullException(nameof(document));
    public string OriginalFileName => Document?.OriginalFileName ?? string.Empty;
    public long FileSizeBytes => Document?.FileSizeBytes ?? 0;
    public DateTime UploadTimestampUtc => Document?.UploadTimestampUtc ?? default;
    public string UploaderUserId => Document?.UploaderUserId ?? string.Empty;
    public string UploaderDisplayName => Document?.UploaderDisplayName ?? string.Empty;
    public DrawVerify.Core.Enums.DocumentType DocumentType => Document?.DocumentType ?? DrawVerify.Core.Enums.DocumentType.Unknown;
    public DrawVerify.Core.Enums.UploadStatus UploadStatus => Document?.UploadStatus ?? DrawVerify.Core.Enums.UploadStatus.Pending;
    public DrawVerify.Core.Enums.GeometryStatus GeometryStatus => Document?.GeometryStatus ?? DrawVerify.Core.Enums.GeometryStatus.NotExtracted;
    public string? GeometryError => Document?.GeometryError;
    public int CurrentVersion => Document?.CurrentVersion ?? 0;
}
