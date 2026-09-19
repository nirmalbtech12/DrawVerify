using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteViewerAccessRepositoryTests
{
    [Fact(DisplayName = "TSD-AC05-LIST projects user and group grant metadata")]
    public void Grant_projection_preserves_metadata_and_members()
    {
        var member = new ViewerAccessTarget(ViewerAccessTargetKind.User, "viewer-1", "Viewer One");
        var grant = new ViewerAccessGrant { Target = new ViewerAccessTarget(ViewerAccessTargetKind.Group, "production", "Production Team"), GrantedByName = "Approver", GrantedUtc = DateTime.UtcNow, Members = new[] { member } };
        Assert.Equal("Production Team", grant.Target.DisplayName);
        Assert.Equal("Approver", grant.GrantedByName);
        Assert.Contains(member, grant.Members);
    }

}
