using System.Text;

namespace DrawVerify.Application.DrawingUpload;

public static class DrawingFileValidator
{
    public const string UnsupportedMessage = "Only PDF, PNG, JPG/JPEG, and TIFF/TIF drawing files are supported.";
    public const string InvalidMessage = "The uploaded PDF or image drawing appears to be invalid or corrupt.";

    public static bool HasValidExtension(string name) => Path.GetExtension(name).ToLowerInvariant() is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff";

    public static bool IsWithinSizeLimit(long fileSizeBytes, int maximumFileSizeMb) => fileSizeBytes >= 0 && fileSizeBytes <= (long)maximumFileSizeMb * 1024 * 1024;

    public static async Task<DrawingFileValidationResult> ValidateAsync(Stream content, string fileName, long expectedSize, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (!content.CanRead || !content.CanSeek || !HasValidExtension(fileName)) return DrawingFileValidationResult.Invalid(UnsupportedMessage);
        var originalPosition = content.Position;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            if (expectedSize <= 0 || expectedSize != content.Length) return DrawingFileValidationResult.Invalid(InvalidMessage);
            var header = new byte[(int)Math.Min(content.Length, 16)];
            content.Position = 0;
            var read = await content.ReadAsync(header.AsMemory(), linked.Token).ConfigureAwait(false);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var valid = extension == ".pdf" ? StartsWith(header, read, new byte[] { 0x25, 0x50, 0x44, 0x46 })
                : extension == ".png" ? StartsWith(header, read, new byte[] { 0x89, 0x50, 0x4e, 0x47 })
                : extension is ".jpg" or ".jpeg" ? read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff
                : extension is ".tif" or ".tiff" && read >= 4 && ((header[0] == 'I' && header[1] == 'I' && header[2] == 42 && header[3] == 0) || (header[0] == 'M' && header[1] == 'M' && header[2] == 0 && header[3] == 42));
            return valid ? DrawingFileValidationResult.Valid() : DrawingFileValidationResult.Invalid(InvalidMessage);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { return DrawingFileValidationResult.Invalid("Drawing validation timed out."); }
        finally { content.Position = originalPosition; }
    }

    private static bool StartsWith(byte[] value, int length, byte[] prefix) => length >= prefix.Length && prefix.SequenceEqual(value.Take(prefix.Length));
}

public sealed record DrawingFileValidationResult(bool IsValid, string Message)
{
    public static DrawingFileValidationResult Valid() => new(true, string.Empty);
    public static DrawingFileValidationResult Invalid(string message) => new(false, message);
}
