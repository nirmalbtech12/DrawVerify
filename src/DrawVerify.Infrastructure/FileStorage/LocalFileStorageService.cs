namespace DrawVerify.Infrastructure.FileStorage;

/// <summary>
/// File system storage for raw drawing files.
/// </summary>
public sealed class LocalFileStorageService
{
    public LocalFileStorageService(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
    public string Root { get; }
}
