using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class SignatureStampMetadataTests
{
    [Fact(DisplayName = "TSD-04 stamp metadata contains signer role timestamp revision placeholder and custom fields")]
    public void Stamp_metadata_is_populated_from_signing_context()
    {
        var placeholderId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var signature = SignatureData.CreateApprover(SignatureMethod.Typed, "approval", "approver-1", "Approver One", Guid.NewGuid(), 4, timestamp, placeholderId: placeholderId, customFields: new Dictionary<string, string> { ["Department"] = "Quality" });

        Assert.Equal("Approver One", signature.SignerDisplayName);
        Assert.Equal("Approver", signature.Role);
        Assert.Equal(timestamp, signature.TimestampUtc);
        Assert.Equal(4, signature.Revision);
        Assert.Equal(placeholderId, signature.PlaceholderId);
        Assert.Equal("Quality", signature.CustomFields["Department"]);
        Assert.True(signature.IsTamperEvident);
    }

    [Fact(DisplayName = "TSD-04 missing required stamp identity fails")]
    public void Missing_document_or_revision_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", Guid.Empty, 1, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", Guid.NewGuid(), 0, DateTimeOffset.UtcNow));
    }
}
