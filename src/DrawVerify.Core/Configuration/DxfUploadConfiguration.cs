namespace DrawVerify.Core.Configuration;

public sealed class DxfUploadConfiguration
{
    public int MaxFileSizeMb { get; init; } = 50;
    public string StorageFolder { get; init; } = "uploads/dxf";
    public string DuplicatePolicy { get; init; } = "BlockAndConfirm";
    public int ValidationTimeoutSeconds { get; init; } = 2;

    public long MaxFileSizeBytes => checked((long)MaxFileSizeMb * 1024L * 1024L);

    public void Validate()
    {
        if (MaxFileSizeMb <= 0)
            throw new InvalidOperationException("DXF upload MaxFileSizeMb must be a positive integer.");
        if (string.IsNullOrWhiteSpace(StorageFolder) || Path.IsPathRooted(StorageFolder) ||
            StorageFolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part == ".."))
            throw new InvalidOperationException("DXF upload storage folder must be a contained relative path.");
        if (!string.Equals(DuplicatePolicy, "BlockAndConfirm", StringComparison.Ordinal))
            throw new InvalidOperationException("DXF upload duplicate policy must be BlockAndConfirm.");
        if (ValidationTimeoutSeconds <= 0)
            throw new InvalidOperationException("DXF upload validation timeout must be a positive integer.");
    }
}
