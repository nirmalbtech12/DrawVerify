using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using DrawVerify.App.Controls;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalApprovalStampControlTests
{
    [Fact(DisplayName = "T13 optional branding retains required approval metadata")]
    public async Task T13_branding_is_optional_but_required_metadata_is_renderable()
    {
        var branded = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", organizationName: "DrawVerify", approvalText: "Internally Approved");
        var unbranded = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", organizationName: null);
        branded.Validate(); unbranded.Validate();
        Assert.Equal("DrawVerify", branded.OrganizationName);
        Assert.Null(unbranded.OrganizationName);
        Assert.Equal("Internally Approved", branded.ApprovalText);
        Assert.Equal("Internally Approved", unbranded.ApprovalText);

        await RunOnStaAsync(() =>
        {
            var control = new SignatureStampControl
            {
                IsApprover = true,
                DocumentId = Document,
                Revision = 1,
                MakerUserId = branded.ApproverUserId,
                MakerDisplayName = branded.ApproverDisplayName,
                FinalizedSignature = SignatureData.CreateApprover(SignatureMethod.Typed, "approval", branded.ApproverUserId, branded.ApproverDisplayName, Document, 1, Applied)
            };
            Assert.True(control.IsSignatureVisible);
            Assert.False(control.CanEdit);
            Assert.Contains(branded.ApproverDisplayName, control.MetadataText);
        });
    }

    [Fact(DisplayName = "T14 stamp placement uses a designated approval area")]
    public async Task T14_stamp_has_designated_placement_and_does_not_change_content_bounds()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", placement: "Approval");
        stamp.Validate();
        Assert.Equal("Approval", stamp.Placement);
        Assert.DoesNotContain("DrawingContent", stamp.Placement);

        await RunOnStaAsync(() =>
        {
            var control = new SignatureStampControl { IsApprover = true, DocumentId = Document, Revision = 1 };
            Assert.Equal(Document, control.DocumentId);
            Assert.Equal(1, control.Revision);
            Assert.Equal("Approver Signature", control.SignatureLabel);
        });
    }

    private static async Task RunOnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(null); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (thread.IsAlive) thread.Join(TimeSpan.FromSeconds(1));
    }

    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
}
