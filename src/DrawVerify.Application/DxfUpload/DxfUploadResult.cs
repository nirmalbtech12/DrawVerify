using DrawVerify.Core.Models;

namespace DrawVerify.Application.DxfUpload;

public enum DxfUploadResultKind
{
    Success,
    Rejected,
    Duplicate,
    Cancelled,
    Unauthorized,
    Failed
}

public sealed class DxfUploadResult
{
    public DxfUploadResultKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public Document? Document { get; init; }
    public DxfUploadPreparation? Preparation { get; init; }
    public string? OriginalFileName { get; init; }
    public long FileSizeBytes { get; init; }
    public bool IsDuplicate => Kind == DxfUploadResultKind.Duplicate;

    public static DxfUploadResult Rejected(string message) => new()
    {
        Kind = DxfUploadResultKind.Rejected,
        Message = message
    };
}
