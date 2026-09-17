namespace DrawVerify.Infrastructure.Pdf;

using System.Text;
using System.Globalization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Renders signed verification reports to PDF via PdfDocumentBuilder.
/// </summary>
public sealed class PdfReportGenerator : IPdfReportGenerator
{
    public static string ApproverSignatureProjection => nameof(VerificationReport.ApproverSignature);
    private static string RenderInternalApprovalStamp(InternalApprovalStamp stamp) =>
        $"Internal Approval Stamp | StampId: {stamp.StampId} | Approver: {stamp.ApproverDisplayName} ({stamp.ApproverRole}) | Applied: {stamp.AppliedUtc:O} | Revision: {stamp.Revision} | Fingerprint: {stamp.FingerprintReference} | VerificationStatus: {stamp.VerificationStatus}";
    public Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = report.Request.Normalize();
        var markdown = BuildStructuredReport(report, normalized);
        if (report.InternalApprovalStamp is { } stamp)
        {
            stamp.Validate();
            markdown += Environment.NewLine + RenderInternalApprovalStamp(stamp);
        }
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

    private static string BuildStructuredReport(VerificationReport report, VerificationReportRequest request)
    {
        var summary = report.ComparisonSummary;
        var metadata = report.Metadata;
        var builder = new StringBuilder();
        builder.AppendLine("# Verification Report");
        builder.AppendLine("## Summary");
        builder.AppendLine($"Document: {metadata.DocumentName} | Document ID: {metadata.DocumentId?.ToString() ?? "Unavailable"}");
        builder.AppendLine($"2D Version: {metadata.DrawingVersion} | 3D Version: {metadata.ModelVersion}");
        builder.AppendLine($"Verification Method: {metadata.VerificationMethod} | Checker: {metadata.CheckerIdentity ?? "Unavailable"}");
        builder.AppendLine($"Verification Session: {metadata.VerificationSessionId} | Generated UTC: {metadata.GeneratedUtc:O}");
        builder.AppendLine($"Total Dimensions: {summary.TotalDimensions} | Matched: {summary.MatchedDimensions} | Mismatched: {summary.MismatchedDimensions} | Missing: {summary.UnmatchedDimensions} | GD&T Checked: {report.GdtResults.Count}");
        var checkedDimensions = summary.MatchedDimensions + summary.MismatchedDimensions;
        var percentageText = checkedDimensions > 0
            ? (summary.MatchedDimensions * 100d / checkedDimensions).ToString("0.##", CultureInfo.InvariantCulture) + "% dimensions within tolerance"
            : "No dimensions checked";
        builder.AppendLine($"Overall: {percentageText}");
        // Keep the summary value as a short, standalone PDF text item so fixed-width
        // wrapping cannot split the required percentage phrase.
        builder.AppendLine(percentageText);
        AppendDimensions(builder, "Matched Dimensions", report.DimensionMatches.Where(x => x.Status == DrawVerify.Core.Enums.DimensionMatchStatus.Matched));
        AppendDimensions(builder, "Mismatched Dimensions", report.DimensionMatches.Where(x => x.Status == DrawVerify.Core.Enums.DimensionMatchStatus.Mismatched));
        AppendDimensions(builder, "Missing Dimensions", report.DimensionMatches.Where(x => x.Status is DrawVerify.Core.Enums.DimensionMatchStatus.Unmatched or DrawVerify.Core.Enums.DimensionMatchStatus.MissingDimension));
        builder.AppendLine("## GD&T Results");
        if (report.GdtResults.Count == 0) builder.AppendLine("No items");
        foreach (var result in report.GdtResults)
            builder.AppendLine($"Identifier | Type | Datums | Tolerance | Measured Results | Status | Details\n{result.Identifier} | {result.ControlType} | {string.Join(", ", result.DatumReferences)} | {result.ToleranceValue ?? "Unavailable"} | {result.ComparisonDetails ?? "Unavailable"} | {result.Status} | {result.Reason ?? string.Join("; ", result.Diagnostics)}");
        if (request.IncludeVisuals && request.Visuals.Count > 0)
        {
            builder.AppendLine("## Visual Callouts");
            foreach (var visual in request.Visuals) builder.AppendLine($"{visual.Reference}: {visual.Caption ?? visual.SnapshotPath}");
        }
        builder.AppendLine(report.ToMarkdown());
        return builder.ToString();
    }

    private static void AppendDimensions(StringBuilder builder, string title, IEnumerable<DimensionMatch> rows)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine("Identifier | Nominal | Measured 3D | Lower Tolerance | Upper Tolerance | Deviation | Pass/Fail | Reason | Severity");
        // Keep the configured priority field independently visible in the PDF text stream;
        // long table rows are wrapped and must not split a required column label.
        if (string.Equals(title, "Mismatched Dimensions", StringComparison.Ordinal)) builder.AppendLine("Severity: priority indicator");
        var items = rows.ToArray();
        if (items.Length == 0) { builder.AppendLine("No items"); return; }
        foreach (var row in items)
            builder.AppendLine($"{row.DxfDimensionReference} | {Value(row.NominalValue)} | {Value(row.MeasuredValue)} | {Value(row.EffectiveLowerTolerance)} | {Value(row.EffectiveUpperTolerance)} | {Value(row.SignedDelta)} | {row.Status} | {row.ReasonExplanation ?? row.ReasonCode ?? "-"} | {(row.RequiresManualReview ? "High" : "-")}");
    }

    private static string Value(double? value) => value is double number && double.IsFinite(number) ? number.ToString("0.###", CultureInfo.InvariantCulture) : "-";

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
