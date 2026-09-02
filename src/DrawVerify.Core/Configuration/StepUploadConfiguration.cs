namespace DrawVerify.Core.Configuration;

public sealed class StepUploadConfiguration
{
    public int MaxFileSizeMb { get; init; } = 50;
    public string StorageFolder { get; init; } = "uploads/step";
    public string DuplicatePolicy { get; init; } = "BlockAndConfirm";

    public void Validate()
    {
        if (MaxFileSizeMb <= 0) throw new InvalidOperationException("Step upload MaxFileSizeMb must be a positive integer.");
        if (string.IsNullOrWhiteSpace(StorageFolder) || Path.IsPathRooted(StorageFolder)) throw new InvalidOperationException("Step upload storage folder must be a relative path.");
        if (!string.Equals(DuplicatePolicy, "BlockAndConfirm", StringComparison.Ordinal)) throw new InvalidOperationException("Step upload duplicate policy must be BlockAndConfirm.");
    }
}
