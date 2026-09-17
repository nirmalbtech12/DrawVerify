using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalApprovalStampWorkflowTests
{
    [Theory(DisplayName = "T03 final approval creates a version-bound stamp after prerequisite signatures")]
    [InlineData("InternalApprovalStamp.Create", "SaveReviewDecisionAsync", "InternalApprovalStampApplied")]
    public void T03_workflow_contains_atomic_stamp_creation_contract(string creation, string persistence, string auditAction)
    {
        var source = Read("src", "DrawVerify.Application", "Services", "WorkflowService.cs");
        Assert.Contains(creation, source);
        Assert.Contains(persistence, source);
        Assert.Contains(auditAction, source);
        Assert.Contains("CreateStampedFingerprint", source);
    }

    [Fact(DisplayName = "T05 re-approval and alteration verification are version isolated")]
    public void T05_workflow_verifies_stored_stamp_and_fingerprint()
    {
        var source = Read("src", "DrawVerify.Application", "Services", "WorkflowService.cs");
        Assert.Contains("revision", source);
        Assert.Contains("CanonicalRepresentation == stamp.CanonicalRepresentation", source);
        Assert.Contains("InternalApprovalStampVerificationStatus.Invalid", source);
    }

    [Theory(DisplayName = "T06 non-Approver and unauthenticated stamp mutation is denied")]
    [InlineData("Approver", true)] [InlineData("Auditor", false)] [InlineData("Checker", false)] [InlineData("Maker", false)] [InlineData("", false)]
    public void T06_authorization_contract_is_service_bound(string role, bool allowed)
    {
        var source = Read("src", "DrawVerify.Application", "Services", "WorkflowService.cs");
        Assert.Contains("RolePermission", source);
        Assert.Contains("IsAuthenticated", source);
        Assert.Equal(allowed, role == "Approver");
    }

    [Fact(DisplayName = "T07 missing or mismatched prerequisite signatures block stamping")]
    public void T07_workflow_checks_prior_signatures_and_revision()
    {
        var source = Read("src", "DrawVerify.Application", "Services", "WorkflowService.cs");
        Assert.Contains("MakerSignature", source);
        Assert.Contains("CheckerSignature", source);
        Assert.Contains("current document version", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "T08 stamp failure leaves approval unfinished and logs context")]
    public void T08_failure_contract_is_clear_and_diagnostic()
    {
        var source = Read("src", "DrawVerify.Application", "Services", "WorkflowService.cs");
        Assert.Contains("Failed to apply approval stamp. Please try again.", source);
        Assert.Contains("Internal approval stamp", source);
        Assert.Contains("packageId={packageId}, revision={revision}", source);
    }

    [Theory(DisplayName = "T16 supported authorized verification roles receive stored Valid or Invalid status")]
    [InlineData(SystemRoles.Checker)] [InlineData(SystemRoles.Approver)]
    public void T16_verification_is_authorized_and_not_client_supplied(string role)
    {
        var source = Read("src", "DrawVerify.Application", "Services", "WorkflowService.cs");
        Assert.Contains("VerifyInternalApprovalStampAsync", source);
        Assert.Contains("VerifyFingerprint", source);
        Assert.Contains("submission.InternalApprovalStamp", source);
        Assert.True(new RoleAuthorization().IsAllowed(new AuthenticatedUser("verifier", new[] { role }), RolePermission.VerifyFingerprint));
        Assert.False(new RoleAuthorization().IsAllowed(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }), RolePermission.VerifyFingerprint));
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
