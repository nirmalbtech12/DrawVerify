namespace DrawVerify.Core.Interfaces;

public interface IDxfUploadFileStore
{
    Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default);
    Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        => Task.FromException<Stream>(new NotSupportedException("Read access is not supported by this DXF file store."));
}

public sealed record DxfUploadStagingFile(string RelativePath, Stream Stream) : IAsyncDisposable
{
    public string? SanitizedFileName { get; init; }
    public ValueTask DisposeAsync()
    {
        Stream.Dispose();
        return ValueTask.CompletedTask;
    }
}
