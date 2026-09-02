using DrawVerify.Core.Models;

namespace DrawVerify.Application.StepUpload;

public enum StepUploadResultKind { Success, Rejected, Duplicate, Cancelled, Unauthorized, Failed }

public sealed class StepUploadResult
{
    public StepUploadResultKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public Document? Document { get; init; }
    public string? OriginalFileName { get; init; }
    public long FileSizeBytes { get; init; }
    public bool IsDuplicate => Kind == StepUploadResultKind.Duplicate;
    public static StepUploadResult Rejected(string message) => new() { Kind = StepUploadResultKind.Rejected, Message = message };
}
