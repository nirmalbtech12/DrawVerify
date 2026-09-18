namespace DrawVerify.Core.Models;
public sealed record ReportTemplateColumn { public string Field { get; init; } = "DimensionId"; public string Label { get; init; } = "Dimension ID"; public int Precision { get; init; } = 3; public string? Unit { get; init; } }
