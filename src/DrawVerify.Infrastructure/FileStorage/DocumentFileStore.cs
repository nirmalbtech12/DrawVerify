using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.Infrastructure.FileStorage;

/// <summary>
/// Maps logical documents to stored files on disk.
/// </summary>
public sealed class DocumentFileStore : IStepUploadFileStore
{
<<<<<<< HEAD
    public string Resolve(string storedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedPath);
        if (!File.Exists(storedPath)) throw new FileNotFoundException("Stored STEP file was not found.");
        return storedPath;
    }
=======
    private readonly string _root;
    public DocumentFileStore(string root, StepUploadConfiguration? configuration = null) { var folder=configuration?.StorageFolder ?? "uploads/step"; _root=Path.GetFullPath(Path.Combine(root, folder)); Directory.CreateDirectory(_root); }
    public async Task<StepUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken token = default) { token.ThrowIfCancellationRequested(); var name=Sanitize(originalFileName); if (string.IsNullOrEmpty(name)) throw new ArgumentException("The file name cannot be sanitized to an empty name.",nameof(originalFileName)); var relative=Guid.NewGuid().ToString("N")+".upload"; var full=Contained(relative); var stream=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.SequentialScan); return await Task.FromResult(new StepUploadStagingFile(relative,stream){SanitizedFileName=name}); }
    public async Task<string> MoveToFinalAsync(StepUploadStagingFile staging, CancellationToken token = default) { token.ThrowIfCancellationRequested(); var source=Contained(staging.RelativePath); var name=staging.SanitizedFileName ?? throw new InvalidOperationException("The staging file has no sanitized final name."); var finalRelative=Guid.NewGuid().ToString("N")+"_"+name; var final=Contained(finalRelative); File.Move(source,final); return await Task.FromResult(finalRelative.Replace(Path.DirectorySeparatorChar,'/')); }
    public Task DeleteAsync(string relativePath, CancellationToken token = default) { token.ThrowIfCancellationRequested(); var full=Contained(relativePath); if(File.Exists(full))File.Delete(full); return Task.CompletedTask; }
    private string Contained(string relative) { var full=Path.GetFullPath(Path.Combine(_root,relative)); var prefix=_root.EndsWith(Path.DirectorySeparatorChar)?_root:_root+Path.DirectorySeparatorChar;if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Storage path escapes the configured STEP storage root.");return full; }
    private static string Sanitize(string value) { var leaf=Path.GetFileName(value); var invalid=Path.GetInvalidFileNameChars(); var chars=leaf.Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray(); var clean=new string(chars).Trim(); if(clean.Length>128){var ext=Path.GetExtension(clean);var stem=Path.GetFileNameWithoutExtension(clean);clean=stem[..Math.Max(1,128-ext.Length)]+ext;} return clean; }
>>>>>>> 642060c (KAN-19: implementation)
}
