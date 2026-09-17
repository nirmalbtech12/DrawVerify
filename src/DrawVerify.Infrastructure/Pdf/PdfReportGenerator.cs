namespace DrawVerify.Infrastructure.Pdf;

using System.Text;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Renders signed verification reports to PDF via PdfDocumentBuilder.
/// </summary>
public sealed class PdfReportGenerator : IPdfReportGenerator
{
    public static string ApproverSignatureProjection => nameof(VerificationReport.ApproverSignature);
    public Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        var markdown = report.ToMarkdown();
        var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .SelectMany(line => Wrap(line, 108))
            .ToArray();
        var stream = new StringBuilder();
        stream.Append("BT /F1 9 Tf 40 760 Td ");
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) stream.Append("0 -12 Td ");
            stream.Append('(').Append(Escape(lines[index])).Append(") Tj ");
        }
        stream.Append("ET");
        return Task.FromResult(BuildDocument(stream.ToString()));
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        if (value.Length == 0) return new[] { string.Empty };
        return Enumerable.Range(0, (value.Length + width - 1) / width).Select(index => value.Substring(index * width, Math.Min(width, value.Length - index * width)));
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal).Select(character => character is >= ' ' and <= '~' ? character : '?').Aggregate(new StringBuilder(), (builder, character) => builder.Append(character)).ToString();

    private static byte[] BuildDocument(string content)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var result = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var (value, index) in objects.Select((value, index) => (value, index + 1)))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(result.ToString()));
            result.Append(index).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(result.ToString());
        result.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++) result.Append(offsets[index].ToString("D10")).Append(" 00000 n \n");
        result.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(result.ToString());
    }
}
