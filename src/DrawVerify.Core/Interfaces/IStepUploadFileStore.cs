namespace DrawVerify.Core.Interfaces;

public interface IStepUploadFileStore
{
    Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default);
    Task<string> MoveToFinalAsync(StepUploadStagingFile stagingFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}

public sealed record StepUploadStagingFile(string RelativePath, Stream Stream) : IAsyncDisposable
{
    public string? SanitizedFileName { get; init; }
    public ValueTask DisposeAsync() { Stream.Dispose(); return ValueTask.CompletedTask; }
}
