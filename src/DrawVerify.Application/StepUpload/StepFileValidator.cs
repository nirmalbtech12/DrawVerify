namespace DrawVerify.Application.StepUpload;

public static class StepFileValidator
{
    public const string InvalidMessage = "The uploaded STEP file is invalid or corrupt. Please check the file and try again.";
    public static bool HasValidExtension(string name) => string.Equals(Path.GetExtension(name), ".step", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(name), ".stp", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(long fileSizeBytes, int maximumFileSizeMb) =>
        fileSizeBytes >= 0 && fileSizeBytes <= (long)maximumFileSizeMb * 1024 * 1024;

    public static async Task<bool> IsValidAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (!content.CanRead || !content.CanSeek) return false;
        var originalPosition = content.Position;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = new byte[1];
            await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException) { return false; }
        finally { content.Position = originalPosition; }
    }
}
