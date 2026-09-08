using DrawVerify.Core.Enums;
namespace DrawVerify.Core.Models;
public sealed record VerificationSession
{
    public Guid VerificationSessionId { get; init; } = Guid.NewGuid();
    public Guid StepDocumentId { get; init; }
    public Guid DxfDocumentId { get; init; }
    public Guid? StepDocumentVersionId { get; init; }
    public Guid? DxfDocumentVersionId { get; init; }
    public VerificationSessionStatus Status { get; init; } = VerificationSessionStatus.Pending;
    public string? ErrorMessage { get; init; }
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}
