using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class DimensionMatchingDomainTests
{
    [Fact]
    public void Legacy_dimension_constructor_defaults_radial_kind_to_unknown()
    {
        var dimension = new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 1, DimensionType.Radial, 5);
        Assert.Equal(RadialDimensionKind.Unknown, dimension.RadialKind);
    }

    [Fact]
    public void Configuration_rejects_invalid_limits_and_thresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionMatchingConfiguration { TimeoutSeconds = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionMatchingConfiguration { MaximumStepFileSizeBytes = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new DimensionMatchingConfiguration { HighConfidenceThreshold = .5, MediumConfidenceThreshold = .75 }.Validate());
    }

    [Fact]
    public void Only_maker_and_checker_can_match_or_view_results()
    {
        var authorization = new RoleAuthorization();
        Assert.False(authorization.IsAllowed(null, RolePermission.MatchDimensionsToGeometry));
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("maker", new[] { "Maker" }), RolePermission.MatchDimensionsToGeometry));
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("checker", new[] { "Checker" }), RolePermission.ViewDimensionMatchResults));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("viewer", new[] { "Viewer" }), RolePermission.ViewDimensionMatchResults));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("approver", new[] { "Approver" }), RolePermission.MatchDimensionsToGeometry));
    }

    [Fact]
    public void Match_record_requires_explicit_status_and_supports_manual_review_fields()
    {
        var match = new DimensionMatch { VerificationSessionId = Guid.NewGuid(), Status = DimensionMatchStatus.Unmatched, RequiresManualReview = true, ReasonCode = "NoCorrespondingGeometryFound" };
        Assert.Equal(DimensionMatchStatus.Unmatched, match.Status);
        Assert.True(match.RequiresManualReview);
        Assert.Equal("NoCorrespondingGeometryFound", match.ReasonCode);
    }
}
