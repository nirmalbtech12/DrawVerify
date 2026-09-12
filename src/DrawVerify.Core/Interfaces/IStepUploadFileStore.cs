namespace DrawVerify.Core.Interfaces;

public interface IStepUploadFileStore
{
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromException<Stream>(new NotSupportedException("Read access is not supported by this STEP file store."));
    Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default);
    Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}

public sealed record StepUploadStagingFile(string RelativePath, Stream Stream) : IAsyncDisposable
{
    public string? SanitizedFileName { get; init; }
    public ValueTask DisposeAsync() { Stream.Dispose(); return ValueTask.CompletedTask; }
}
