namespace DrawVerify.Core.Models;
public sealed record ReportTemplateVersion { public Guid TemplateId { get; init; } public int Version { get; init; } public DateTimeOffset CreatedUtc { get; init; } public ReportTemplate Snapshot { get; init; } = new(); }
