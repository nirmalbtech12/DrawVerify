using DrawVerify.Core.Models;

namespace DrawVerify.Application.DrawingUpload;

public enum DrawingUploadResultKind { Success, Rejected, Duplicate, Cancelled, Failed }

public sealed class DrawingUploadResult
{
    public DrawingUploadResultKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public Document? Document { get; init; }
    public string? OriginalFileName { get; init; }
    public long FileSizeBytes { get; init; }
    public bool IsDuplicate => Kind == DrawingUploadResultKind.Duplicate;
    public static DrawingUploadResult Rejected(string message) => new() { Kind = DrawingUploadResultKind.Rejected, Message = message };
}
