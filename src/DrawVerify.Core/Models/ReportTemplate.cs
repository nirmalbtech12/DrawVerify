using System.Text.Json.Serialization;

namespace DrawVerify.Core.Models;

public sealed record ReportTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Organization Report";
    public int Version { get; init; } = 1;
    public bool IsOrganizationDefault { get; init; }
    public DateTimeOffset EffectiveDate { get; init; } = DateTimeOffset.UtcNow;
    public ReportTemplateHeader Header { get; init; } = new();
    public ReportTemplateFooter Footer { get; init; } = new();
    public IReadOnlyList<ReportTemplateSection> Sections { get; init; } = ReportTemplateDefaults.Sections;
    public IReadOnlyList<ReportTemplateTable> Tables { get; init; } = ReportTemplateDefaults.Tables;
    public ReportTemplateSummary Summary { get; init; } = new();
    public ReportTemplateStyle Style { get; init; } = new();
    public string? ProjectId { get; init; }
    public string? WorkflowId { get; init; }
    public ReportTemplateSource? Source { get; init; }
    public IReadOnlyList<TemplatePlaceholder> Placeholders { get; init; } = [];
    public void Validate() => ApplicationValidation.Validate(this);
}

internal static class ApplicationValidation
{
    public static void Validate(ReportTemplate template)
    {
        if (template.Id == Guid.Empty) throw new InvalidOperationException("Template ID is required.");
        if (string.IsNullOrWhiteSpace(template.Name)) throw new InvalidOperationException("Template name is required.");
        if (template.Version < 1) throw new InvalidOperationException("Template version must be positive.");
        if (template.Sections.Count == 0 || template.Sections.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != template.Sections.Count || template.Sections.Select(x => x.Ordinal).Distinct().Count() != template.Sections.Count)
            throw new InvalidOperationException("Template sections must have unique keys and ordinals.");
        if (template.Sections.Any(x => !ReportTemplateDefaults.SectionKeys.Contains(x.Key, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(x.Title) || x.Ordinal < 0)) throw new InvalidOperationException("Template contains an invalid section.");
        foreach (var table in template.Tables)
        {
            if (!ReportTemplateDefaults.TableKeys.Contains(table.Key, StringComparer.Ordinal)) throw new InvalidOperationException($"Unknown report table '{table.Key}'.");
            if (table.Columns.Select(x => x.Field).Distinct(StringComparer.Ordinal).Count() != table.Columns.Count || table.Columns.Any(x => !ReportTemplateDefaults.ColumnKeys.Contains(x.Field, StringComparer.Ordinal) || x.Precision is < 0 or > 10)) throw new InvalidOperationException($"Invalid columns in report table '{table.Key}'.");
        }
        foreach (var statistic in template.Summary.Statistics)
            if (!ReportTemplateDefaults.StatisticKeys.Contains(statistic.Key, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(statistic.Label)) throw new InvalidOperationException($"Invalid summary statistic '{statistic.Key}'.");
        if (!ReportTemplateDefaults.Layouts.Contains(template.Summary.Layout, StringComparer.Ordinal)) throw new InvalidOperationException("Invalid summary layout.");
        template.Style.Validate();
        if (template.Footer.PageNumberFormat is not null && (template.Footer.PageNumberFormat.Contains('{') || !template.Footer.PageNumberFormat.Contains('X'))) throw new InvalidOperationException("Page number format must use the X and Y placeholders.");
    }
}

public static class ReportTemplateDefaults
{
    public static readonly string[] SectionKeys = ["Summary", "MatchedDimensions", "MismatchedDimensions", "MissingDimensions", "GdtResults", "AuditTrail"];
    public static readonly string[] TableKeys = ["Dimensions", "Gdt"];
    public static readonly string[] ColumnKeys = ["DimensionId", "Nominal", "Measured", "Tolerance", "Deviation", "Status", "ControlType", "Details"];
    public static readonly string[] StatisticKeys = ["TotalDimensions", "PassRate", "MismatchCount", "MissingCount", "GdtCount"];
    public static readonly string[] Layouts = ["Table", "BulletList", "KeyMetrics"];
    public static IReadOnlyList<ReportTemplateSection> Sections => SectionKeys.Select((key, i) => new ReportTemplateSection { Key = key, Ordinal = i, Title = key switch { "GdtResults" => "GD&T Results", "AuditTrail" => "Audit Trail", _ => key } }).ToArray();
    public static IReadOnlyList<ReportTemplateTable> Tables => [new() { Key = "Dimensions", Columns = ColumnKeys.Take(6).Select(x => new ReportTemplateColumn { Field = x, Label = x }).ToArray() }, new() { Key = "Gdt", Columns = ColumnKeys.Skip(6).Select(x => new ReportTemplateColumn { Field = x, Label = x }).ToArray() }];
}
