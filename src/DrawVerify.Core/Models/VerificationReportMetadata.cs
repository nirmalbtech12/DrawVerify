namespace DrawVerify.Core.Models;

public sealed record VerificationReportMetadata
{
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
}
