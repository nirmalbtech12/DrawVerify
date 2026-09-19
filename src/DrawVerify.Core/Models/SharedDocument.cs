using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record SharedDocument
{
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public string DocumentName { get; init; } = string.Empty;
    public string DocumentNumber => DocumentName;
    public int Revision { get; init; }
    public string RevisionText => Revision.ToString();
    public WorkflowStatus ApprovalStatus { get; init; }
    public DateTime? FinalApprovalUtc { get; init; }
    public string ApproverName { get; init; } = string.Empty;
    public string ApproverUserId { get; init; } = string.Empty;
    public string SharedByName { get; init; } = string.Empty;
    public string SharedByUserId { get; init; } = string.Empty;
    public DateTime SharedUtc { get; init; }
    public string? SharingNote { get; init; }
    public DrawVerify.Core.Enums.DocumentType DocumentType { get; init; }
    public string StoredRelativePath { get; init; } = string.Empty;
    public SignatureData? ApproverSignature { get; init; }
    public InternalApprovalStamp? ApprovalStamp { get; init; }
    public string? ApprovalInformationError { get; init; }
}

public sealed record SharedDocumentAccess(SharedDocument Document, Stream Content);
