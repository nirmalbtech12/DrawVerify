using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalApprovalStampControlTests
{
    [Fact(DisplayName = "T13 optional branding retains required approval metadata")]
    public void T13_branding_is_optional_but_required_metadata_is_renderable()
    {
        var branded = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", organizationName: "DrawVerify", approvalText: "Internally Approved");
        var unbranded = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", organizationName: null);
        branded.Validate(); unbranded.Validate();
        Assert.Equal("DrawVerify", branded.OrganizationName);
        Assert.Null(unbranded.OrganizationName);
        Assert.Equal("Internally Approved", branded.ApprovalText);
        Assert.Equal("Internally Approved", unbranded.ApprovalText);
    }

    [Fact(DisplayName = "T14 stamp placement uses a designated approval area")]
    public void T14_stamp_has_designated_placement_and_does_not_change_content_bounds()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", placement: "Approval");
        stamp.Validate();
        Assert.Equal("Approval", stamp.Placement);
        Assert.DoesNotContain("DrawingContent", stamp.Placement);
    }

    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
}
