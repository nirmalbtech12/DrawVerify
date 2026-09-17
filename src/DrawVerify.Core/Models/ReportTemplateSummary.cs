namespace DrawVerify.Core.Models;
public sealed record ReportTemplateSummary { public string Layout { get; init; } = "Table"; public IReadOnlyList<ReportTemplateStatistic> Statistics { get; init; } = [new() { Key = "TotalDimensions", Label = "Total dimensions" }, new() { Key = "PassRate", Label = "Pass rate", Format = "0.##%" }, new() { Key = "MismatchCount", Label = "Mismatches" }]; }
