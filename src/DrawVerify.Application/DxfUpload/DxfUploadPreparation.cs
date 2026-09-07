using DrawVerify.Core.Models;

namespace DrawVerify.Application.DxfUpload;

public sealed class DxfUploadPreparation
{
    public Guid ConfirmationToken { get; init; } = Guid.NewGuid();
    public string OriginalFileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public Document? DuplicateDocument { get; init; }
    public bool IsDuplicate => DuplicateDocument is not null;
}
