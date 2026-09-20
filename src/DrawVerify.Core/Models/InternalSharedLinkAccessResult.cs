namespace DrawVerify.Core.Models;

public enum InternalSharedLinkAccessFailure { None, AuthenticationRequired, InvalidLink, Expired, Inactive, Unauthorized, DocumentNotApproved, DocumentNotFound, VersionMismatch, AuditFailed }

public sealed record InternalSharedLinkAccessResult
{
    public bool Succeeded { get; init; }
    public InternalSharedLinkAccessFailure Failure { get; init; }
    public string Message { get; init; } = string.Empty;
    public InternalSharedLink? Link { get; init; }
    public Document? Document { get; init; }
    public static InternalSharedLinkAccessResult Fail(InternalSharedLinkAccessFailure failure, string message) => new() { Failure = failure, Message = message };
}
