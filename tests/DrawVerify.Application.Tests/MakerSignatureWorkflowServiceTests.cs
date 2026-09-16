using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MakerSignatureWorkflowServiceTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset SignedAt = new(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "TD-KAN64-APP-001 only the authenticated Maker can finalize")]
    public void Finalize_requires_matching_maker_identity_and_role()
    {
        var signature = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", Document, 1, SignedAt);
        var maker = new FakeSession(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }) { UserId = "maker-001", DisplayName = "Maker One" });
        Assert.Equal(signature, new SignatureService(maker).Finalize(signature));

        var checker = new FakeSession(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker" });
        Assert.Throws<UnauthorizedAccessException>(() => new SignatureService(checker).Finalize(signature));
        Assert.Throws<UnauthorizedAccessException>(() => new SignatureService(maker).Finalize(signature with { MakerUserId = "other-maker" }));
    }

    [Fact(DisplayName = "TD-KAN64-APP-001 submission request carries finalized signature data")]
    public void Submission_request_preserves_signature_and_revision_data()
    {
        var signature = SignatureData.Create(SignatureMethod.Uploaded, "png-bytes", "maker-001", "Maker One", Document, 2, SignedAt, "Engineering", "Design");
        var request = new SubmissionRequest(Document) { MakerSignature = signature };
        Assert.NotNull(request.MakerSignature);
        Assert.Equal(2, request.MakerSignature!.Revision);
        Assert.Equal(signature.PayloadHash, request.MakerSignature.PayloadHash);
        Assert.Equal(SystemRoles.Maker, request.MakerSignature.Role);
    }

    [Fact(DisplayName = "TD-KAN64-APP-001 signature state changes fingerprint metadata inputs")]
    public void Signature_metadata_is_distinct_for_fingerprint_context()
    {
        var typed = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", Document, 1, SignedAt);
        var drawn = SignatureData.Create(SignatureMethod.Drawn, "stroke:1,2;3,4", "maker-001", "Maker One", Document, 1, SignedAt);
        var typedContext = new DocumentFingerprintContext(Document, 1, Guid.NewGuid(), Guid.NewGuid(), new Dictionary<string, string> { ["signatureHash"] = typed.PayloadHash, ["signatureMethod"] = typed.Method.ToString(), ["signatureTimestampUtc"] = typed.TimestampUtc.ToString("O") });
        var drawnContext = typedContext with { Metadata = new Dictionary<string, string> { ["signatureHash"] = drawn.PayloadHash, ["signatureMethod"] = drawn.Method.ToString(), ["signatureTimestampUtc"] = drawn.TimestampUtc.ToString("O") } };
        Assert.NotEqual(typedContext.Metadata["signatureHash"], drawnContext.Metadata["signatureHash"]);
        Assert.NotEqual(typedContext.Metadata["signatureMethod"], drawnContext.Metadata["signatureMethod"]);
    }

    private sealed class FakeSession(AuthenticatedUser? user) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser => user;
        public bool IsAuthenticated => user is not null;
        public void SignIn(AuthenticatedUser authenticatedUser) => throw new NotSupportedException();
        public void SignOut() => throw new NotSupportedException();
    }
}
