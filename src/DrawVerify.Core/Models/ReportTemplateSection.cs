namespace DrawVerify.Core.Models;
public sealed record ReportTemplateSection { public string Key { get; init; } = "Summary"; public bool Enabled { get; init; } = true; public int Ordinal { get; init; } public string Title { get; init; } = "Summary"; public string Description { get; init; } = ""; }
