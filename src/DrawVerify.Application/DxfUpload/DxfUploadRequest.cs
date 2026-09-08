namespace DrawVerify.Application.DxfUpload;

public sealed class DxfUploadRequest
{
    public DxfUploadRequest(string originalFileName, Stream content, long? fileSizeBytes = null)
    {
        OriginalFileName = originalFileName ?? throw new ArgumentNullException(nameof(originalFileName));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        FileSizeBytes = fileSizeBytes;
    }

    public string OriginalFileName { get; }
    public Stream Content { get; }
    public long? FileSizeBytes { get; }
    public IProgress<long>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
