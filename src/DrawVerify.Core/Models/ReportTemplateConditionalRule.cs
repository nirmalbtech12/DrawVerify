namespace DrawVerify.Core.Models;
public sealed record ReportTemplateConditionalRule { public string Field { get; init; } = "Status"; public string Operator { get; init; } = "Equals"; public string Value { get; init; } = "Mismatched"; public string Color { get; init; } = "#FF0000"; }
