namespace DrawVerify.Core.Models;
public sealed record ReportTemplateAssignment { public Guid TemplateId { get; init; } public string? ProjectId { get; init; } public string? WorkflowId { get; init; } public DateTimeOffset CreatedUtc { get; init; } }
