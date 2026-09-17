namespace DrawVerify.Core.Models;

public sealed record VerificationReportMetadata
{
    public string ReportType { get; init; } = "VerificationReportWithAuditTrail";
    public string DocumentName { get; init; } = "Unavailable";
    public Guid? DocumentId { get; init; }
    public string DrawingVersion { get; init; } = "Unavailable";
    public string ModelVersion { get; init; } = "Unavailable";
    public Guid? DrawingVersionId { get; init; }
    public Guid? ModelVersionId { get; init; }
    public Guid VerificationSessionId { get; init; }
    public string VerificationMethod { get; init; } = "Unavailable";
    public string? CheckerIdentity { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string GeneratorIdentity { get; init; } = "Unavailable";
    public string AuditScope { get; init; } = "Complete audit trail";
}
