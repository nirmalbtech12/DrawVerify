namespace DrawVerify.Infrastructure.FileStorage;

/// <summary>
/// Maps logical documents to stored files on disk.
/// </summary>
public sealed class DocumentFileStore
{
    public string Resolve(string storedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);
        if (!File.Exists(storedPath)) throw new FileNotFoundException("Stored STEP file was not found.");
        return storedPath;
    }
}
