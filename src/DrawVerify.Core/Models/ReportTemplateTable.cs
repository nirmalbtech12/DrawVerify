namespace DrawVerify.Core.Models;
public sealed record ReportTemplateTable { public string Key { get; init; } = "Dimensions"; public IReadOnlyList<ReportTemplateColumn> Columns { get; init; } = Array.Empty<ReportTemplateColumn>(); public IReadOnlyList<ReportTemplateConditionalRule> ConditionalRules { get; init; } = Array.Empty<ReportTemplateConditionalRule>(); }
