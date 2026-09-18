namespace DrawVerify.Core.Models;
public sealed record ReportTemplateFooter { public string? Text { get; init; } public string? PageNumberFormat { get; init; } public string? TemplateVersionLabel { get; init; } public DateTimeOffset? EffectiveDate { get; init; } }
