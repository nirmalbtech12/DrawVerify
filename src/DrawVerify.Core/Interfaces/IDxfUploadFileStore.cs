namespace DrawVerify.Core.Interfaces;

public interface IDxfUploadFileStore
{
    Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default);
    Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
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
