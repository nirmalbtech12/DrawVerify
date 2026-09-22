using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class SignatureDataTests
{
    [Fact(DisplayName = "TSD-04 canonical signature representation includes dynamic stamp fields")]
    public void Canonical_signature_representation_contains_dynamic_fields()
    {
        var documentId = Guid.NewGuid();
        var placeholderId = Guid.NewGuid();
        var signature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker", "checker-1", "Checker One", documentId, 7, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), placeholderId: placeholderId, customFields: new Dictionary<string, string> { ["RevisionLabel"] = "R7" });

        Assert.Contains("Checker One", signature.CanonicalRepresentation);
        Assert.Contains("Checker", signature.CanonicalRepresentation);
        Assert.Contains(documentId.ToString(), signature.CanonicalRepresentation);
        Assert.Contains("7", signature.CanonicalRepresentation);
        Assert.Contains(placeholderId.ToString("D"), signature.CanonicalRepresentation);
        Assert.Contains("RevisionLabel=R7", signature.CanonicalRepresentation);
        Assert.True(signature.IsTamperEvident);
    }

    [Fact(DisplayName = "TSD-04 legacy signature data remains readable")]
    public void Legacy_signature_without_placeholder_metadata_validates()
    {
        var signature = SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        Assert.Null(signature.PlaceholderId);
        signature.Validate();
    }
}
