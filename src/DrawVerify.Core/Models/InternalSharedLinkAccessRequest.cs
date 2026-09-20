namespace DrawVerify.Core.Models;

public sealed record InternalSharedLinkAccessRequest(string Token, Guid? ExpectedDocumentId = null, Guid? ExpectedVersionId = null, bool Download = false);
