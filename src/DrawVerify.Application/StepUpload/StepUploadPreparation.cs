using DrawVerify.Core.Models;

namespace DrawVerify.Application.StepUpload;

public sealed class StepUploadPreparation
{
    public string OriginalFileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public Document? DuplicateDocument { get; init; }
    public bool IsDuplicate => DuplicateDocument is not null;
}
