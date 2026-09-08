using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.Infrastructure.FileStorage;

public sealed class DxfDocumentFileStore : IDxfUploadFileStore
{
    private readonly string _root;

    public DxfDocumentFileStore(string root, DxfUploadConfiguration? configuration = null)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A storage root is required.", nameof(root));

        configuration ??= new DxfUploadConfiguration();
        configuration.Validate();
        _root = Path.GetFullPath(Path.Combine(root, configuration.StorageFolder));
        Directory.CreateDirectory(_root);
    }

    public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sanitized = Sanitize(originalFileName);
        if (string.IsNullOrEmpty(sanitized))
            throw new ArgumentException("The file name cannot be sanitized to an empty name.", nameof(originalFileName));

        var relativePath = Guid.NewGuid().ToString("N") + ".upload";
        var fullPath = Contained(relativePath);
        var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(new DxfUploadStagingFile(relativePath, stream) { SanitizedFileName = sanitized });
    }

    public async Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Contained(stagingFile.RelativePath);
        var sanitized = stagingFile.SanitizedFileName ?? throw new InvalidOperationException("The staging file has no sanitized final name.");
        var relativePath = Guid.NewGuid().ToString("N") + "_" + sanitized;
        var destination = Contained(relativePath);
        await stagingFile.DisposeAsync().ConfigureAwait(false);
        File.Move(source, destination);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Contained(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string Contained(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A storage path is required.", nameof(relativePath));
        var normalizedRelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_root, normalizedRelativePath));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Storage path escapes the configured DXF storage root.");
        return fullPath;
    }

    private static string Sanitize(string originalFileName)
    {
        var leaf = Path.GetFileName(originalFileName);
        var invalid = Path.GetInvalidFileNameChars();
        const string platformIndependentInvalidCharacters = "<>:\"/\\|?*";
        var characters = leaf.Select(character => invalid.Contains(character) || platformIndependentInvalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character).ToArray();
        var sanitized = new string(characters).Trim();
        if (sanitized.Length <= 128)
            return sanitized;

        var extension = Path.GetExtension(sanitized);
        var stem = Path.GetFileNameWithoutExtension(sanitized);
        var maximumStemLength = Math.Max(1, 128 - extension.Length);
        return stem[..Math.Min(stem.Length, maximumStemLength)] + extension;
    }
}
