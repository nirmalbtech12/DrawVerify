namespace DrawVerify.Infrastructure.Pdf;

using System.Text;
using System.Globalization;
using System.IO.Compression;
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
        var logoPath = string.IsNullOrWhiteSpace(template.Header.LogoAssetPath) ? null : ResolveManagedLogo(template.Header.LogoAssetPath);
        var markdown = BuildStructuredReport(report with { Metadata = report.Metadata with { TemplateId = template.Id, TemplateVersion = template.Version } }, normalized, template);
        if (report.InternalApprovalStamp is { } stamp)
        {
            stamp.Validate();
            markdown += Environment.NewLine + RenderInternalApprovalStamp(stamp);
        }
        var pages = RenderPages(markdown, cancellationToken, template, logoPath);
        return Task.FromResult(BuildDocument(pages, template, logoPath));
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
                var logoPath = ResolveManagedLogo(template.Header.LogoAssetPath);
                builder.AppendLine($"Logo: managed branding asset embedded ({Path.GetFileName(logoPath)})");
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

    private static IReadOnlyList<string> RenderPages(string markdown, CancellationToken cancellationToken, ReportTemplate? template = null, string? logoPath = null)
    {
        var margin = template?.Style.Margin ?? 40;
        var pageHeight = template?.Style.Orientation == "Landscape" ? 612d : 792d;
        var lineHeight = Math.Max(12d, (template?.Style.BodyFontSize ?? 9d) + 3d);
        var linesPerPage = Math.Max(1, (int)Math.Floor((pageHeight - (margin * 2) - 30) / lineHeight));
        var pageWidth = template?.Style.Orientation == "Landscape" ? 792d : 612d;
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
                var fontSize = IsHeading(line) ? template?.Style.HeadingFontSize ?? 14 : IsTableContent(line) ? template?.Style.TableFontSize ?? 8 : template?.Style.BodyFontSize ?? 9;
                if (currentLineCount == 0)
                {
                    currentPage.Append("BT /F1 ").Append(fontSize.ToString(CultureInfo.InvariantCulture)).Append(" Tf ").Append(TextX(line, template, margin, pageWidth, fontSize).ToString(CultureInfo.InvariantCulture)).Append(" ").Append((pageHeight - margin).ToString(CultureInfo.InvariantCulture)).Append(" Td ");
                    if (logoPath is not null) currentPage.Append(LogoPlacement(template!, margin, pageWidth, pageHeight));
                }
                else currentPage.Append("0 -").Append(lineHeight.ToString(CultureInfo.InvariantCulture)).Append(" Td ");
                if (template is not null)
                {
                    var conditionalColor = ConditionalColor(line, template);
                    currentPage.Append(ColorOperator(conditionalColor ?? (IsTableContent(line) ? template.Style.BorderColor : template.Style.HeaderColor)));
                    if (IsTableContent(line)) currentPage.Append("q ").Append(ColorOperator(template.Style.BorderColor).Replace(" rg ", " RG ", StringComparison.Ordinal)).Append("0.5 w ").Append(margin.ToString(CultureInfo.InvariantCulture)).Append(' ').Append((pageHeight - margin - ((currentLineCount + 1) * lineHeight)).ToString(CultureInfo.InvariantCulture)).Append(" 500 12 re S Q ");
                }
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
                if (!string.IsNullOrWhiteSpace(footerText)) pages[index].Append("BT /F1 9 Tf ").Append(margin.ToString(CultureInfo.InvariantCulture)).Append(" ").Append(margin.ToString(CultureInfo.InvariantCulture)).Append(" Td (").Append(Escape(footerText)).Append(") Tj ");
                if (!string.IsNullOrWhiteSpace(footer.PageNumberFormat)) pages[index].Append("BT /F1 9 Tf ").Append((pageWidth - margin - 100).ToString(CultureInfo.InvariantCulture)).Append(" ").Append(margin.ToString(CultureInfo.InvariantCulture)).Append(" Td (").Append(Escape(footer.PageNumberFormat.Replace("X", (index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("Y", pages.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))).Append(") Tj ");
                pages[index].Append("ET");
            }
        }
        return pages.Select(page => page.ToString()).ToArray();
    }

    private static bool IsHeading(string line) => line.StartsWith('#');
    private static bool IsTableContent(string line) => line.StartsWith("Table:", StringComparison.Ordinal) || line.Contains(" | ", StringComparison.Ordinal);
    private static string? ConditionalColor(string line, ReportTemplate template) => template.Tables.SelectMany(x => x.ConditionalRules).FirstOrDefault(rule => MatchesRule(line, rule))?.Color;
    private static bool MatchesRule(string line, ReportTemplateConditionalRule rule)
    {
        if (rule.Field == "Status" && rule.Operator == "Equals") return line.Contains(rule.Value, StringComparison.OrdinalIgnoreCase);
        if (rule.Operator == "Contains") return line.Contains(rule.Value, StringComparison.OrdinalIgnoreCase);
        if (rule.Field is "Nominal" or "Measured" or "Tolerance" or "Deviation" && double.TryParse(rule.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
        {
            var values = line.Split('|').Select(value => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? (double?)number : null).Where(value => value.HasValue).Select(value => value!.Value);
            return rule.Operator switch { "GreaterThan" => values.Any(value => value > expected), "LessThan" => values.Any(value => value < expected), "Equals" => values.Any(value => Math.Abs(value - expected) < 0.000001), _ => false };
        }
        return false;
    }
    private static string ResolveManagedLogo(string reference)
    {
        if (Path.IsPathFullyQualified(reference)) throw new InvalidOperationException("Configured report template logo must use a managed asset reference.");
        var normalized = reference.Replace('\\', '/');
        const string prefix = "report-template-branding/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal) || normalized.Contains("/../", StringComparison.Ordinal)) throw new InvalidOperationException("Configured report template logo is outside the managed branding store.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "report-template-branding"));
        var path = Path.GetFullPath(Path.Combine(root, normalized[prefix.Length..]));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Configured report template logo is outside the managed branding store.");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp") throw new InvalidOperationException("Configured report template logo format is unsupported.");
        if (!File.Exists(path)) throw new InvalidOperationException($"Configured report template logo asset was not found: {reference}");
        if (new FileInfo(path).Length > 5 * 1024 * 1024) throw new InvalidOperationException("Configured report template logo asset exceeds the 5 MB limit.");
        return path;
    }

    private static double TextX(string line, ReportTemplate? template, double margin, double pageWidth, double fontSize)
    {
        if (template is null || !IsHeading(line) && !line.StartsWith("Organization:", StringComparison.Ordinal)) return margin;
        var estimatedWidth = line.Length * fontSize * 0.5d;
        return template.Header.TextAlignment switch { "Center" => Math.Max(margin, (pageWidth - estimatedWidth) / 2d), "Right" => Math.Max(margin, pageWidth - margin - estimatedWidth), _ => margin };
    }

    private static string LogoPlacement(ReportTemplate template, double margin, double pageWidth, double pageHeight)
    {
        var x = template.Header.LogoPosition switch { "Center" => pageWidth / 2 - 36, "Right" => pageWidth - margin - 72, _ => margin };
        return $"q 72 0 0 36 {x.ToString(CultureInfo.InvariantCulture)} {(pageHeight - margin - 48).ToString(CultureInfo.InvariantCulture)} cm /Im1 Do Q ";
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        if (value.Length == 0) return new[] { string.Empty };
        return Enumerable.Range(0, (value.Length + width - 1) / width).Select(index => value.Substring(index * width, Math.Min(width, value.Length - index * width)));
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal).Select(character => character is >= ' ' and <= '~' ? character : '?').Aggregate(new StringBuilder(), (builder, character) => builder.Append(character)).ToString();

    private static byte[] BuildDocument(IReadOnlyList<string> pageContents, ReportTemplate? template = null, string? logoPath = null)
    {
        if (pageContents.Count == 0) throw new InvalidOperationException("A PDF report must contain at least one page.");
        var pageObjects = Enumerable.Range(0, pageContents.Count).Select(index => 3 + index).ToArray();
        var contentObjects = Enumerable.Range(0, pageContents.Count).Select(index => 3 + pageContents.Count + index).ToArray();
        var firstAuxiliaryObject = 3 + pageContents.Count * 2;
        var imageObject = logoPath is null ? 0 : firstAuxiliaryObject;
        var embeddedAssetObject = logoPath is null ? 0 : firstAuxiliaryObject + 1;
        var fontObject = firstAuxiliaryObject + (logoPath is null ? 0 : 2);
        var objects = new List<string>
        {
            logoPath is null
                ? "<< /Type /Catalog /Pages 2 0 R >>"
                : $"<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(managed-logo) {embeddedAssetObject} 0 R] >> >> >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObjects.Select(id => $"{id} 0 R"))}] /Count {pageContents.Count} >>"
        };
        for (var index = 0; index < pageContents.Count; index++)
        {
            var landscape = template?.Style.Orientation == "Landscape";
            var width = landscape ? 792 : 612; var height = landscape ? 612 : 792;
            var resources = logoPath is null
                ? $"/Font << /F1 {fontObject} 0 R >>"
                : $"/Font << /F1 {fontObject} 0 R >> /XObject << /Im1 {imageObject} 0 R >>";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << {resources} >> /Contents {contentObjects[index]} 0 R >>");
        }
        foreach (var content in pageContents)
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        if (logoPath is not null)
        {
            var logoBytes = File.ReadAllBytes(logoPath);
            var logoHex = Convert.ToHexString(logoBytes);
            objects.Add(BuildImageObject(logoPath, logoBytes, embeddedAssetObject));
            objects.Add($"<< /Type /EmbeddedFile /Subtype /application#2Foctet-stream /Filter /ASCIIHexDecode /Length {logoHex.Length + 1} >>\nstream\n{logoHex}>\nendstream");
        }
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
        return Encoding.Latin1.GetBytes(result.ToString());
    }

    private static string BuildImageObject(string logoPath, byte[] logoBytes, int embeddedAssetObject)
    {
        var extension = Path.GetExtension(logoPath).ToLowerInvariant();
        if (extension is ".jpg" or ".jpeg")
            return $"<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {logoBytes.Length} /AssetObject {embeddedAssetObject} 0 R >>\nstream\n{Encoding.Latin1.GetString(logoBytes)}\nendstream";

        var (width, height, pixels) = extension == ".png" ? DecodePng(logoBytes) : DecodeBmp(logoBytes);
        var pixelHex = Convert.ToHexString(pixels);
        return $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length {pixelHex.Length + 1} /AssetObject {embeddedAssetObject} 0 R >>\nstream\n{pixelHex}>\nendstream";
    }

    private static (int Width, int Height, byte[] Pixels) DecodePng(byte[] bytes)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("Configured report template logo is not a valid PNG asset.");
        var width = ReadBigEndianInt32(bytes, 16);
        var height = ReadBigEndianInt32(bytes, 20);
        if (width <= 0 || height <= 0 || bytes[24] != 8 || bytes[25] is not (2 or 6))
            throw new InvalidOperationException("Configured report template PNG must use 8-bit RGB or RGBA pixels.");
        using var compressed = new MemoryStream();
        var offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            var length = ReadBigEndianInt32(bytes, offset);
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (length < 0 || offset + 12 + length > bytes.Length) throw new InvalidOperationException("Configured report template PNG is truncated.");
            if (type == "IHDR" && bytes[offset + 9] != 0) throw new InvalidOperationException("Configured report template PNG uses unsupported compression settings.");
            if (type == "IDAT") compressed.Write(bytes, offset + 8, length);
            offset += length + 12;
            if (type == "IEND") break;
        }
        compressed.Position = 0;
        using var decoded = new MemoryStream();
        using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress, leaveOpen: true)) inflater.CopyTo(decoded);
        var channels = bytes[25] == 6 ? 4 : 3;
        var stride = checked(width * channels);
        var scanlines = decoded.ToArray();
        if (scanlines.Length != checked((stride + 1) * height)) throw new InvalidOperationException("Configured report template PNG pixel data is invalid.");
        var previous = new byte[stride];
        var rgb = new byte[checked(width * height * 3)];
        var source = 0; var destination = 0;
        for (var row = 0; row < height; row++)
        {
            var filter = scanlines[source++];
            var current = scanlines.AsSpan(source, stride).ToArray(); source += stride;
            for (var i = 0; i < stride; i++)
            {
                byte left = i >= channels ? current[i - channels] : (byte)0;
                var up = previous[i];
                byte upperLeft = i >= channels ? previous[i - channels] : (byte)0;
                current[i] = filter switch { 0 => current[i], 1 => (byte)(current[i] + left), 2 => (byte)(current[i] + up), 3 => (byte)(current[i] + ((left + up) / 2)), 4 => (byte)(current[i] + Paeth(left, up, upperLeft)), _ => throw new InvalidOperationException("Configured report template PNG uses an unsupported filter.") };
            }
            for (var pixel = 0; pixel < width; pixel++) { rgb[destination++] = current[pixel * channels]; rgb[destination++] = current[pixel * channels + 1]; rgb[destination++] = current[pixel * channels + 2]; }
            previous = current;
        }
        return (width, height, rgb);
    }

    private static (int Width, int Height, byte[] Pixels) DecodeBmp(byte[] bytes)
    {
        if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M') throw new InvalidOperationException("Configured report template logo is not a valid BMP asset.");
        var offset = BitConverter.ToInt32(bytes, 10); var width = BitConverter.ToInt32(bytes, 18); var signedHeight = BitConverter.ToInt32(bytes, 22); var bits = BitConverter.ToInt16(bytes, 28);
        if (width <= 0 || signedHeight == 0 || bits is not (24 or 32)) throw new InvalidOperationException("Configured report template BMP must use 24-bit or 32-bit pixels.");
        var height = Math.Abs(signedHeight); var bytesPerPixel = bits / 8; var stride = ((width * bytesPerPixel + 3) / 4) * 4; var rgb = new byte[checked(width * height * 3)];
        for (var y = 0; y < height; y++) { var sourceY = signedHeight > 0 ? height - 1 - y : y; for (var x = 0; x < width; x++) { var source = offset + sourceY * stride + x * bytesPerPixel; var destination = (y * width + x) * 3; rgb[destination] = bytes[source + 2]; rgb[destination + 1] = bytes[source + 1]; rgb[destination + 2] = bytes[source]; } }
        return (width, height, rgb);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    private static byte Paeth(byte a, byte b, byte c) { var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }

    private static string ColorOperator(string color)
    {
        var r = int.Parse(color[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var g = int.Parse(color[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var b = int.Parse(color[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return $"{r:0.###} {g:0.###} {b:0.###} rg ";
    }
}
