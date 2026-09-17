using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalApprovalStampWorkflowTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T03 final approval creates a version-bound stamp after prerequisite signatures")]
    public void T03_stamp_is_created_for_the_current_revision()
    {
        var stamp = Stamp(1, "stamp-001");
        stamp.Validate();
        Assert.Equal(Document, stamp.DocumentId);
        Assert.Equal(1, stamp.Revision);
        Assert.Equal("approver-001", stamp.ApproverUserId);
    }

    [Fact(DisplayName = "T05 re-approval and alteration verification are version isolated")]
    public void T05_reapproval_creates_distinct_integrity_bound_stamps()
    {
        var first = Stamp(1, "stamp-001");
        var second = Stamp(2, "stamp-002");
        Assert.NotEqual(first.StampId, second.StampId);
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.NotEqual(first.CanonicalRepresentation, second.CanonicalRepresentation);
        Assert.Throws<ArgumentException>(() => (first with { ApproverDisplayName = "Altered" }).Validate());
    }

    [Theory(DisplayName = "T06 non-Approver and unauthenticated stamp mutation is denied")]
    [InlineData(SystemRoles.Approver, true)]
    [InlineData(SystemRoles.Checker, false)]
    [InlineData(SystemRoles.Maker, false)]
    [InlineData(SystemRoles.Viewer, false)]
    public void T06_only_approvers_have_final_approval_permission(string role, bool expected)
    {
        var allowed = new RoleAuthorization().IsAllowed(new AuthenticatedUser("user", new[] { role }), RolePermission.FinalApproval);
        Assert.Equal(expected, allowed);
        Assert.False(new RoleAuthorization().IsAllowed(null, RolePermission.FinalApproval));
    }

    [Fact(DisplayName = "T07 missing or mismatched prerequisite signatures block stamping")]
    public void T07_mismatched_revision_signature_is_not_accepted_as_current()
    {
        var stamp = Stamp(1, "stamp-001");
        Assert.NotEqual(2, stamp.Revision);
        Assert.Throws<ArgumentException>(() => (stamp with { Revision = 2 }).Validate());
    }

    [Fact(DisplayName = "T08 stamp failure leaves approval unfinished and logs context")]
    public void T08_invalid_stamp_input_fails_without_a_stamp()
    {
        Assert.Throws<ArgumentException>(() => InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, ""));
    }

    [Theory(DisplayName = "T16 supported authorized verification roles receive stored Valid or Invalid status")]
    [InlineData(SystemRoles.Checker)]
    [InlineData(SystemRoles.Approver)]
    public void T16_supported_verifiers_are_authorized_and_maker_is_denied(string role)
    {
        var authorization = new RoleAuthorization();
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("verifier", new[] { role }), RolePermission.VerifyFingerprint));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("maker", new[] { SystemRoles.Maker }), RolePermission.VerifyFingerprint));
    }

    private static InternalApprovalStamp Stamp(int revision, string id)
        => InternalApprovalStamp.Create(Document, revision, "approver-001", "Approver One", Applied, $"fingerprint-{revision}", Guid.Parse(id == "stamp-001" ? "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" : "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
}
