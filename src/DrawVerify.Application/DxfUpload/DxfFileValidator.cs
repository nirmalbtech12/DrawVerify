using System.Text;

namespace DrawVerify.Application.DxfUpload;

public static class DxfFileValidator
{
    public const string TypeMessage = "Only DXF files (.dxf) are allowed.";
    public const string NotDxfMessage = TypeMessage;
    public const string SizeMessageFormat = "File exceeds the maximum allowed size of {0} MB.";
    public const string InvalidMessage = "The uploaded DXF file is invalid or corrupt. Please check the file and try again.";
    public const string CorruptMessage = InvalidMessage;

    public static bool HasValidExtension(string name) =>
        string.Equals(Path.GetExtension(name), ".dxf", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(long fileSizeBytes, int maximumFileSizeMb) =>
        fileSizeBytes >= 0 && fileSizeBytes <= (long)maximumFileSizeMb * 1024L * 1024L;

    public static async Task<DxfFileValidationResult> ValidateAsync(
        Stream content,
        long expectedSize,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!content.CanRead || !content.CanSeek)
            return DxfFileValidationResult.Invalid("The DXF stream must be readable and seekable.");

        var originalPosition = content.Position;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            if (expectedSize < 0 || content.Length != expectedSize)
                return DxfFileValidationResult.Invalid("The DXF stream length did not match the declared size.");

            const int markerWindowSize = 8192;
            var firstLength = (int)Math.Min(content.Length, markerWindowSize);
            var first = await ReadAtAsync(content, 0, firstLength, linked.Token).ConfigureAwait(false);
            var firstText = Encoding.ASCII.GetString(first);
            if (!ContainsMarker(firstText, "SECTION"))
                return DxfFileValidationResult.Invalid("Missing SECTION marker.");

            var lastLength = (int)Math.Min(content.Length, markerWindowSize);
            var last = await ReadAtAsync(content, content.Length - lastLength, lastLength, linked.Token).ConfigureAwait(false);
            var lastText = Encoding.ASCII.GetString(last);
            if (!ContainsMarker(lastText, "EOF"))
                return DxfFileValidationResult.Invalid("Missing EOF marker.");

            return DxfFileValidationResult.Valid();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return DxfFileValidationResult.Invalid("DXF validation timed out.");
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static async Task<byte[]> ReadAtAsync(Stream content, long position, int count, CancellationToken cancellationToken)
    {
        content.Position = position;
        var result = new byte[count];
        var read = 0;
        while (read < count)
        {
            var current = await content.ReadAsync(result.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
            if (current == 0)
                break;
            read += current;
        }

        return read == count ? result : result[..read];
    }

    private static bool ContainsMarker(string content, string marker) =>
        content.Split(new[] { ' ', '\t', '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token.Trim(), marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record DxfFileValidationResult(bool IsValid, string Reason, string Message)
{
    public static DxfFileValidationResult Valid() => new(true, string.Empty, string.Empty);
    public static DxfFileValidationResult Invalid(string reason) => new(false, reason, DxfFileValidator.InvalidMessage);
}
