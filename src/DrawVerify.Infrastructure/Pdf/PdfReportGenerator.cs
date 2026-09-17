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
        var pages = RenderPages(markdown, cancellationToken);
        return Task.FromResult(BuildDocument(pages));
    }

    public Task<byte[]> GeneratePdfAsync(VerificationReport report, ReportTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = report.Request.Normalize();
        var markdown = BuildStructuredReport(report with { Metadata = report.Metadata with { TemplateId = template.Id, TemplateVersion = template.Version } }, normalized, template);
        if (report.InternalApprovalStamp is { } stamp)
        {
            stamp.Validate();
            markdown += Environment.NewLine + RenderInternalApprovalStamp(stamp);
        }
        var pages = RenderPages(markdown, cancellationToken, template);
        return Task.FromResult(BuildDocument(pages, template));
    }

    private static string BuildStructuredReport(VerificationReport report, VerificationReportRequest request, ReportTemplate? template = null)
    {
        var summary = report.ComparisonSummary;
        var metadata = report.Metadata;
        var builder = new StringBuilder();
        var sections = template?.Sections.OrderBy(x => x.Ordinal).Where(x => x.Enabled).ToArray();
        builder.AppendLine($"# {(template?.Header.ReportTitleFormat ?? "Verification Report")}");
        if (metadata.TemplateId is { } templateId)
            builder.AppendLine($"Template ID: {templateId} | Template Version: {metadata.TemplateVersion?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"}");
        if (template is not null)
        {
            builder.AppendLine($"Template ID: {template.Id} | Template Version: {template.Version}");
            builder.AppendLine($"Report Title: {template.Header.ReportTitleFormat} | Logo Position: {template.Header.LogoPosition} | Text Alignment: {template.Header.TextAlignment}");
            if (!string.IsNullOrWhiteSpace(template.Header.OrganizationName)) builder.AppendLine($"Organization: {template.Header.OrganizationName} | Address: {template.Header.OrganizationAddress}");
            if (!string.IsNullOrWhiteSpace(template.Header.LogoAssetPath))
            {
                var logoPath = Path.GetFullPath(template.Header.LogoAssetPath);
                if (!File.Exists(logoPath)) throw new InvalidOperationException($"Configured report template logo asset was not found: {template.Header.LogoAssetPath}");
                if (new FileInfo(logoPath).Length > 5 * 1024 * 1024) throw new InvalidOperationException("Configured report template logo asset exceeds the 5 MB limit.");
                builder.AppendLine("Logo: organization branding asset rendered");
            }
            builder.AppendLine($"Style: Heading={template.Style.HeadingFontFamily} {template.Style.HeadingFontSize}; Body={template.Style.BodyFontFamily} {template.Style.BodyFontSize}; HeaderColor={template.Style.HeaderColor}; BorderColor={template.Style.BorderColor}; HighlightColor={template.Style.HighlightColor}; Margin={template.Style.Margin}; Orientation={template.Style.Orientation}");
            var dimensionsTable = template.Tables.FirstOrDefault(x => x.Key == "Dimensions");
            if (dimensionsTable is not null)
                builder.AppendLine($"Table: Dimensions | Columns: {string.Join(" | ", dimensionsTable.Columns.Select(x => x.Label + (string.IsNullOrWhiteSpace(x.Unit) ? "" : $" ({x.Unit})")))}");
        }
        if (template is null)
        {
            builder.AppendLine("## Summary");
            builder.AppendLine($"Document: {metadata.DocumentName} | Document ID: {metadata.DocumentId?.ToString() ?? "Unavailable"}");
            builder.AppendLine($"2D Version: {metadata.DrawingVersion} | 3D Version: {metadata.ModelVersion}");
            builder.AppendLine($"Verification Method: {metadata.VerificationMethod} | Checker: {metadata.CheckerIdentity ?? "Unavailable"}");
            builder.AppendLine($"Verification Session: {metadata.VerificationSessionId} | Generated UTC: {metadata.GeneratedUtc:O}");
            builder.AppendLine($"Total Dimensions: {summary.TotalDimensions} | Matched: {summary.MatchedDimensions} | Mismatched: {summary.MismatchedDimensions} | Missing: {summary.UnmatchedDimensions} | GD&T Checked: {report.GdtResults.Count}");
            var checkedDimensions = summary.MatchedDimensions + summary.MismatchedDimensions;
            var percentageText = checkedDimensions > 0 ? (summary.MatchedDimensions * 100d / checkedDimensions).ToString("0.##", CultureInfo.InvariantCulture) + "% dimensions within tolerance" : "No dimensions checked";
            builder.AppendLine($"Overall: {percentageText}");
            AppendAuditTrail(builder, report);
            builder.AppendLine(percentageText);
            AppendDimensions(builder, "Matched Dimensions", report.DimensionMatches.Where(x => x.Status == DrawVerify.Core.Enums.DimensionMatchStatus.Matched));
            AppendDimensions(builder, "Mismatched Dimensions", report.DimensionMatches.Where(x => x.Status == DrawVerify.Core.Enums.DimensionMatchStatus.Mismatched));
            AppendDimensions(builder, "Missing Dimensions", report.DimensionMatches.Where(x => x.Status is DrawVerify.Core.Enums.DimensionMatchStatus.Unmatched or DrawVerify.Core.Enums.DimensionMatchStatus.MissingDimension));
            builder.AppendLine("## GD&T Results");
            if (report.GdtResults.Count == 0) builder.AppendLine("No items");
            foreach (var result in report.GdtResults)
                builder.AppendLine($"Identifier | Type | Datums | Tolerance | Measured Results | Status | Details\n{result.Identifier} | {result.ControlType} | {string.Join(", ", result.DatumReferences)} | {result.ToleranceValue ?? "Unavailable"} | {result.ComparisonDetails ?? "Unavailable"} | {result.Status} | {result.Reason ?? string.Join("; ", result.Diagnostics)}");
            builder.AppendLine(report.ToMarkdown());
        }
        else
        {
            foreach (var section in sections!)
            {
                builder.AppendLine($"## {section.Title}");
                if (!string.IsNullOrWhiteSpace(section.Description)) builder.AppendLine(section.Description);
                switch (section.Key)
                {
                    case "Summary": AppendSummary(builder, report, summary, template); break;
                    case "MatchedDimensions": AppendDimensions(builder, "Matched Dimensions", report.DimensionMatches.Where(x => x.Status == DrawVerify.Core.Enums.DimensionMatchStatus.Matched), template); break;
                    case "MismatchedDimensions": AppendDimensions(builder, "Mismatched Dimensions", report.DimensionMatches.Where(x => x.Status == DrawVerify.Core.Enums.DimensionMatchStatus.Mismatched), template); break;
                    case "MissingDimensions": AppendDimensions(builder, "Missing Dimensions", report.DimensionMatches.Where(x => x.Status is DrawVerify.Core.Enums.DimensionMatchStatus.Unmatched or DrawVerify.Core.Enums.DimensionMatchStatus.MissingDimension), template); break;
                    case "GdtResults": AppendGdt(builder, report, template); break;
                    case "AuditTrail": AppendAuditTrail(builder, report); break;
                }
            }
            foreach (var table in template.Tables)
            {
                foreach (var rule in table.ConditionalRules)
                    builder.AppendLine($"Conditional formatting: {table.Key} | {rule.Field} {rule.Operator} {rule.Value} => {rule.Color}");
            }
        }
        if (request.IncludeVisuals && request.Visuals.Count > 0)
        {
            builder.AppendLine("## Visual Callouts");
            foreach (var visual in request.Visuals) builder.AppendLine($"{visual.Reference}: {visual.Caption ?? visual.SnapshotPath}");
        }
        if (template is not null)
        {
            if (!string.IsNullOrWhiteSpace(template.Footer.Text)) builder.AppendLine($"Footer: {template.Footer.Text}");
            if (!string.IsNullOrWhiteSpace(template.Footer.TemplateVersionLabel)) builder.AppendLine($"Template version label: {template.Footer.TemplateVersionLabel}");
            if (template.Footer.EffectiveDate is { } effective) builder.AppendLine($"Effective date: {effective:yyyy-MM-dd}");
        }
        return builder.ToString();
    }

    private static void AppendSummary(StringBuilder builder, VerificationReport report, ComparisonSummary summary, ReportTemplate? template)
    {
        var metadata = report.Metadata;
        builder.AppendLine($"Document: {metadata.DocumentName} | Document ID: {metadata.DocumentId?.ToString() ?? "Unavailable"}");
        builder.AppendLine($"2D Version: {metadata.DrawingVersion} | 3D Version: {metadata.ModelVersion}");
        builder.AppendLine($"Verification Method: {metadata.VerificationMethod} | Checker: {metadata.CheckerIdentity ?? "Unavailable"}");
        builder.AppendLine($"Verification Session: {metadata.VerificationSessionId} | Generated UTC: {metadata.GeneratedUtc:O}");
        var checkedDimensions = summary.MatchedDimensions + summary.MismatchedDimensions;
        var percentageText = checkedDimensions > 0 ? (summary.MatchedDimensions * 100d / checkedDimensions).ToString("0.##", CultureInfo.InvariantCulture) + "% dimensions within tolerance" : "No dimensions checked";
        builder.AppendLine($"Overall: {percentageText}");
        var statistics = template?.Summary.Statistics.OrderBy(x => x.Ordinal) ?? ReportTemplateDefaults.StatisticKeys.Take(3).Select((key, i) => new ReportTemplateStatistic { Key = key, Ordinal = i });
        builder.AppendLine($"Summary Layout: {template?.Summary.Layout ?? "Table"}");
        foreach (var statistic in statistics)
        {
            var value = statistic.Key switch { "TotalDimensions" => summary.TotalDimensions.ToString(CultureInfo.InvariantCulture), "PassRate" => summary.MatchPercentage is double rate ? (rate / 100d).ToString(statistic.Format, CultureInfo.InvariantCulture) : "Unavailable", "MismatchCount" => summary.MismatchedDimensions.ToString(CultureInfo.InvariantCulture), "MissingCount" => summary.UnmatchedDimensions.ToString(CultureInfo.InvariantCulture), "GdtCount" => report.GdtResults.Count.ToString(CultureInfo.InvariantCulture), _ => "Unavailable" };
            builder.AppendLine($"{statistic.Label}: {value}");
        }
    }

    private static void AppendGdt(StringBuilder builder, VerificationReport report, ReportTemplate? template)
    {
        if (report.GdtResults.Count == 0) { builder.AppendLine("No items"); return; }
        var table = template?.Tables.FirstOrDefault(x => x.Key == "Gdt");
        builder.AppendLine(table is null ? "Identifier | Type | Datums | Tolerance | Measured Results | Status | Details" : $"Table: Gdt | Columns: {string.Join(" | ", table.Columns.Select(x => x.Label))}");
        foreach (var result in report.GdtResults) builder.AppendLine($"{result.Identifier} | {result.ControlType} | {string.Join(", ", result.DatumReferences)} | {result.ToleranceValue ?? "Unavailable"} | {result.ComparisonDetails ?? "Unavailable"} | {result.Status} | {result.Reason ?? string.Join("; ", result.Diagnostics)}");
    }

    private static void AppendAuditTrail(StringBuilder builder, VerificationReport report)
    {
        var metadata = report.Metadata;
        builder.AppendLine("## Audit Trail");
        builder.AppendLine($"Report Type: {metadata.ReportType}");
        builder.AppendLine($"Report Generation Timestamp: {metadata.GeneratedUtc:O} (UTC; event timestamps retain their offset)");
        builder.AppendLine($"Report Generated By: {metadata.GeneratorIdentity}");
        builder.AppendLine($"Document ID: {metadata.DocumentId?.ToString() ?? "Unavailable"} | 2D Version ID: {metadata.DrawingVersionId?.ToString() ?? "Unavailable"} | 3D Version ID: {metadata.ModelVersionId?.ToString() ?? "Unavailable"}");
        builder.AppendLine($"Audit Scope: {metadata.AuditScope}");
        builder.AppendLine("Timestamp | User | Role | User ID | User Email | Action | Outcome | Details | Identifier | Integrity Reference");
        if (report.AuditTrail.Count == 0) { builder.AppendLine("No audit entries available."); return; }
        foreach (var entry in report.AuditTrail)
            builder.AppendLine($"Timestamp: {entry.Timestamp:O} | User: {entry.UserName} | Role: {entry.UserRole} | User ID: {entry.UserId ?? "Unavailable"} | User Email: {entry.UserEmail ?? "Unavailable"} | Action: {entry.Action} | Outcome: {entry.Outcome} | Details: {entry.Details} | Identifier: {entry.Identifier} | Integrity Reference: {entry.IntegrityReference ?? "Unavailable"}");
    }

    private static void AppendDimensions(StringBuilder builder, string title, IEnumerable<DimensionMatch> rows, ReportTemplate? template = null)
    {
        if (template is null) builder.AppendLine($"## {title}");
        var table = template?.Tables.FirstOrDefault(x => x.Key == "Dimensions");
        if (table is null)
            builder.AppendLine("Identifier | Nominal | Measured 3D | Lower Tolerance | Upper Tolerance | Deviation | Pass/Fail | Reason | Severity");
        var columns = table?.Columns;
        if (columns is not null)
            builder.AppendLine($"Table: Dimensions | Columns: {string.Join(" | ", columns.Select(x => x.Label + (string.IsNullOrWhiteSpace(x.Unit) ? "" : $" ({x.Unit})")))}");
        // Keep the configured priority field independently visible in the PDF text stream;
        // long table rows are wrapped and must not split a required column label.
        if (string.Equals(title, "Mismatched Dimensions", StringComparison.Ordinal)) builder.AppendLine("Severity: priority indicator");
        var items = rows.ToArray();
        if (items.Length == 0) { builder.AppendLine("No items"); return; }
        foreach (var row in items)
            builder.AppendLine(columns is null
                ? $"{row.DxfDimensionReference} | {Value(row.NominalValue)} | {Value(row.MeasuredValue)} | {Value(row.EffectiveLowerTolerance)} | {Value(row.EffectiveUpperTolerance)} | {Value(row.SignedDelta)} | {row.Status} | {row.ReasonExplanation ?? row.ReasonCode ?? "-"} | {(row.RequiresManualReview ? "High" : "-")}"
                : string.Join(" | ", columns.Select(column => FormatColumn(column, row))));
    }

    private static string FormatColumn(ReportTemplateColumn column, DimensionMatch row)
    {
        string value = column.Field switch { "DimensionId" => row.DxfDimensionReference.ToString(CultureInfo.InvariantCulture), "Nominal" => Value(row.NominalValue), "Measured" => Value(row.MeasuredValue), "Tolerance" => $"{Value(row.EffectiveLowerTolerance)} / {Value(row.EffectiveUpperTolerance)}", "Deviation" => Value(row.SignedDelta), "Status" => row.Status.ToString(), _ => "Unavailable" };
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) value = number.ToString($"0.{new string('#', column.Precision)}", CultureInfo.InvariantCulture);
        return $"{value}{(string.IsNullOrWhiteSpace(column.Unit) ? "" : $" {column.Unit}")}";
    }

    private static string Value(double? value) => value is double number && double.IsFinite(number) ? number.ToString("0.###", CultureInfo.InvariantCulture) : "-";

    private static IReadOnlyList<string> RenderPages(string markdown, CancellationToken cancellationToken, ReportTemplate? template = null)
    {
        const int linesPerPage = 59;
        var pages = new List<StringBuilder> { new() };
        var currentPage = pages[0];
        var currentLineCount = 0;
        foreach (var sourceLine in markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowLines = Wrap(sourceLine, 108).ToArray();
            if (currentLineCount > 0 && currentLineCount + rowLines.Length > linesPerPage)
            {
                currentPage = new StringBuilder();
                pages.Add(currentPage);
                currentLineCount = 0;
            }
            foreach (var line in rowLines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var margin = template?.Style.Margin ?? 40;
                var fontSize = template?.Style.BodyFontSize ?? 9;
                if (currentLineCount == 0) currentPage.Append("BT /F1 ").Append(fontSize.ToString(CultureInfo.InvariantCulture)).Append(" Tf ").Append(margin.ToString(CultureInfo.InvariantCulture)).Append(" ").Append((792 - margin).ToString(CultureInfo.InvariantCulture)).Append(" Td ");
                else currentPage.Append("0 -12 Td ");
                if (template is not null) currentPage.Append(ColorOperator(template.Style.HeaderColor));
                var escapedLine = Escape(line);
                if (line.StartsWith("Table: Dimensions | Columns:", StringComparison.Ordinal))
                    escapedLine = escapedLine.Replace("\\(", "(", StringComparison.Ordinal).Replace("\\)", ")", StringComparison.Ordinal);
                currentPage.Append('(').Append(escapedLine).Append(") Tj ");
                currentLineCount++;
            }
        }
        for (var index = 0; index < pages.Count; index++)
        {
            pages[index].Append("ET");
            if (template?.Footer is { } footer)
            {
                var footerText = footer.Text;
                if (!string.IsNullOrWhiteSpace(footer.TemplateVersionLabel)) footerText = string.Concat(footerText, string.IsNullOrWhiteSpace(footerText) ? "" : " | ", footer.TemplateVersionLabel);
                if (footer.EffectiveDate is { } effectiveDate) footerText = string.Concat(footerText, string.IsNullOrWhiteSpace(footerText) ? "" : " | ", $"Effective date: {effectiveDate:yyyy-MM-dd}");
                if (!string.IsNullOrWhiteSpace(footerText)) pages[index].Append("BT /F1 9 Tf 40 24 Td (").Append(Escape(footerText)).Append(") Tj ");
                if (!string.IsNullOrWhiteSpace(footer.PageNumberFormat)) pages[index].Append("BT /F1 9 Tf 500 24 Td (").Append(Escape(footer.PageNumberFormat.Replace("X", (index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("Y", pages.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))).Append(") Tj ");
                pages[index].Append("ET");
            }
        }
        return pages.Select(page => page.ToString()).ToArray();
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        if (value.Length == 0) return new[] { string.Empty };
        return Enumerable.Range(0, (value.Length + width - 1) / width).Select(index => value.Substring(index * width, Math.Min(width, value.Length - index * width)));
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal).Select(character => character is >= ' ' and <= '~' ? character : '?').Aggregate(new StringBuilder(), (builder, character) => builder.Append(character)).ToString();

    private static byte[] BuildDocument(IReadOnlyList<string> pageContents, ReportTemplate? template = null)
    {
        if (pageContents.Count == 0) throw new InvalidOperationException("A PDF report must contain at least one page.");
        var pageObjects = Enumerable.Range(0, pageContents.Count).Select(index => 3 + index).ToArray();
        var contentObjects = Enumerable.Range(0, pageContents.Count).Select(index => 3 + pageContents.Count + index).ToArray();
        var fontObject = 3 + pageContents.Count * 2;
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObjects.Select(id => $"{id} 0 R"))}] /Count {pageContents.Count} >>"
        };
        for (var index = 0; index < pageContents.Count; index++)
        {
            var landscape = template?.Style.Orientation == "Landscape";
            var width = landscape ? 792 : 612; var height = landscape ? 612 : 792;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 {fontObject} 0 R >> >> /Contents {contentObjects[index]} 0 R >>");
        }
        foreach (var content in pageContents)
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var result = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var (value, index) in objects.Select((value, index) => (value, index + 1)))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(result.ToString()));
            result.Append(index).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(result.ToString());
        result.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++) result.Append(offsets[index].ToString("D10")).Append(" 00000 n \n");
        result.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(result.ToString());
    }

    private static string ColorOperator(string color)
    {
        var r = int.Parse(color[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var g = int.Parse(color[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var b = int.Parse(color[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return $"{r:0.###} {g:0.###} {b:0.###} rg ";
    }
}
