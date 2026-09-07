namespace DrawVerify.Application.StepUpload;

public static class StepFileValidator
{
    public const string NotStepMessage = "The uploaded file is not a valid STEP file. Please check the file and try again.";
    public const string InvalidMessage = "The uploaded STEP file appears to be invalid or corrupt. Please check the file and try again.";
    public const string TruncatedMessage = "The uploaded STEP file appears to be incomplete or corrupt. Please check the file and try again.";
    public const string TimeoutMessage = "STEP file validation timed out. Please check the file and try again.";
    public static bool HasValidExtension(string name) => string.Equals(Path.GetExtension(name), ".step", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(name), ".stp", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(long fileSizeBytes, int maximumFileSizeMb) =>
        fileSizeBytes >= 0 && fileSizeBytes <= (long)maximumFileSizeMb * 1024 * 1024;

    public static async Task<StepFileValidationResult> ValidateAsync(Stream content, long expectedSize, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (!content.CanRead || !content.CanSeek) return StepFileValidationResult.Invalid("Invalid header");
        var originalPosition = content.Position;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            if (expectedSize != content.Length) return StepFileValidationResult.Truncated("Truncated file");
            var headerLength = (int)Math.Min(content.Length, 8192);
            var header = await ReadAtAsync(content, 0, headerLength, linked.Token).ConfigureAwait(false);
            var headerText = System.Text.Encoding.ASCII.GetString(header);
            var hasIsoIndicator = headerText.Contains("ISO-10303-21", StringComparison.OrdinalIgnoreCase);
            var hasSchemaIndicator = headerText.Contains("FILE_SCHEMA", StringComparison.OrdinalIgnoreCase);
            if ((LooksClearlyNonStep(header) && !hasIsoIndicator && !hasSchemaIndicator) || (!hasIsoIndicator && !hasSchemaIndicator)) return StepFileValidationResult.NotStep("Not a STEP file");
            if (!hasIsoIndicator || !hasSchemaIndicator)
                return StepFileValidationResult.Invalid("Invalid header");
            var trailerLength = (int)Math.Min(content.Length, 8192);
            var trailer = await ReadAtAsync(content, content.Length - trailerLength, trailerLength, linked.Token).ConfigureAwait(false);
            var trailerText = System.Text.Encoding.ASCII.GetString(trailer);
            return trailerText.Contains("END-ISO-10303-21", StringComparison.OrdinalIgnoreCase)
                ? StepFileValidationResult.Valid()
                : StepFileValidationResult.Truncated("Truncated file");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { return StepFileValidationResult.Timeout("Validation timeout"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        finally { content.Position = originalPosition; }
    }

    private static async Task<byte[]> ReadAtAsync(Stream content, long position, int count, CancellationToken token)
    {
        content.Position = position;
        var result = new byte[count];
        var read = 0;
        while (read < count)
        {
            var current = await content.ReadAsync(result.AsMemory(read, count - read), token).AsTask().WaitAsync(token).ConfigureAwait(false);
            if (current == 0) break;
            read += current;
        }
        return read == count ? result : result[..read];
    }

    private static bool LooksClearlyNonStep(byte[] content) => content.Length >= 4 &&
        ((content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F') ||
         (content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4e && content[3] == 0x47) ||
         (content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff) ||
         content.Take(Math.Min(content.Length, 512)).Any(b => b == 0));
}

public sealed record StepFileValidationResult(bool IsValid, string Reason, string Message)
{
    public static StepFileValidationResult Valid() => new(true, string.Empty, string.Empty);
    public static StepFileValidationResult NotStep(string reason) => new(false, reason, StepFileValidator.NotStepMessage);
    public static StepFileValidationResult Invalid(string reason) => new(false, reason, StepFileValidator.InvalidMessage);
    public static StepFileValidationResult Truncated(string reason) => new(false, reason, StepFileValidator.TruncatedMessage);
    public static StepFileValidationResult Timeout(string reason) => new(false, reason, StepFileValidator.TimeoutMessage);
}
