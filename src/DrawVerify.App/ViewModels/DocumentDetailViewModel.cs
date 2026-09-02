namespace DrawVerify.App.ViewModels;

public sealed class DocumentDetailViewModel
{
    public DocumentDetailViewModel(DrawVerify.Core.Models.Document document) => Document = document;
    public DrawVerify.Core.Models.Document Document { get; }
    public string OriginalFileName => Document.OriginalFileName;
    public long FileSizeBytes => Document.FileSizeBytes;
    public DateTime UploadTimestampUtc => Document.UploadTimestampUtc;
    public string UploaderUserId => Document.UploaderUserId;
    public string UploaderDisplayName => Document.UploaderDisplayName;
    public DrawVerify.Core.Enums.DocumentType DocumentType => Document.DocumentType;
    public DrawVerify.Core.Enums.UploadStatus UploadStatus => Document.UploadStatus;
    public DrawVerify.Core.Enums.GeometryStatus GeometryStatus => Document.GeometryStatus;
    public int CurrentVersion => Document.CurrentVersion;
}
