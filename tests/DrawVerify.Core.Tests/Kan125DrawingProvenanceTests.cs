using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class Kan125DrawingProvenanceTests
{
    [Fact(DisplayName = "TD-10 preserves generated and uploaded provenance")]
    public void Provenance_is_explicit_on_document_and_version()
    {
        var generatedVersion = new DocumentVersion { DocumentId = Guid.NewGuid(), DrawingSource = DrawingSource.Generated };
        var uploaded = new Document { DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, DrawingSource = DrawingSource.Uploaded, Version = generatedVersion };
        Assert.Equal(DrawingSource.Uploaded, uploaded.DrawingSource);
        Assert.Equal(DrawingSource.Generated, generatedVersion.DrawingSource);
        Assert.NotEqual(DrawingSource.Unknown, uploaded.DrawingSource);
    }

    [Fact(DisplayName = "TD-10 grants generation only to Maker")]
    public void Generation_permission_is_maker_only()
    {
        var policy = new RoleAuthorization();
        Assert.True(policy.IsAllowed(new AuthenticatedUser("maker", [SystemRoles.Maker]), RolePermission.GenerateDxfDrawing));
        Assert.False(policy.IsAllowed(new AuthenticatedUser("checker", [SystemRoles.Checker]), RolePermission.GenerateDxfDrawing));
        Assert.False(policy.IsAllowed(null, RolePermission.GenerateDxfDrawing));
    }
}
