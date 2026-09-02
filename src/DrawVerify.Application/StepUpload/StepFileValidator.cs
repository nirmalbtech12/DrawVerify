using System.Text;

namespace DrawVerify.Application.StepUpload;

public static class StepFileValidator
{
    public const string InvalidMessage = "The uploaded STEP file is invalid or corrupt. Please check the file and try again.";
    public static bool HasValidExtension(string name) => string.Equals(Path.GetExtension(name), ".step", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(name), ".stp", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> IsValidAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek) return false;
        var originalPosition = content.Position;
        try
        {
            content.Position = 0;
            using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            var normalized = trimmed.TrimEnd(' ', '\t', '\r', '\n');
            return normalized.StartsWith("ISO-10303-21;", StringComparison.Ordinal) &&
                   normalized.Contains("HEADER;", StringComparison.Ordinal) &&
                   normalized.EndsWith("END-ISO-10303-21;", StringComparison.Ordinal);
        }
        catch (DecoderFallbackException) { return false; }
        finally { content.Position = originalPosition; }
    }
}
