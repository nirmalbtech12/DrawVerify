namespace DrawVerify.Infrastructure.Pdf;

/// <summary>
/// Shared PDF text construction boundary. Report-specific layout remains in PdfReportGenerator.
/// </summary>
internal static class PdfDocumentBuilder
{
    public static string EscapeText(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
}
