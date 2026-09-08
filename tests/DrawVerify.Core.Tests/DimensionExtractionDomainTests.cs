using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class DimensionExtractionDomainTests
{
    [Fact(DisplayName = "TD-KAN24-CORE-001 uses the approved dimension extraction defaults")]
    public void Configuration_defaults_are_valid_and_exposed_by_alias()
    {
        var configuration = new DxfDimensionExtractionConfiguration();

        configuration.Validate();

        Assert.Equal(52_428_800L, configuration.MaximumFileSizeBytes);
        Assert.Equal(configuration.MaximumFileSizeBytes, configuration.MaxFileSizeBytes);
        Assert.Equal(30, configuration.TimeoutSeconds);
    }

    [Theory(DisplayName = "TD-KAN24-CORE-001 rejects non-positive extraction configuration")]
    [InlineData(0L, 30)]
    [InlineData(-1L, 30)]
    [InlineData(52_428_800L, 0)]
    [InlineData(52_428_800L, -1)]
    public void Configuration_rejects_non_positive_values(long maximumFileSizeBytes, int timeoutSeconds)
    {
        var configuration = new DxfDimensionExtractionConfiguration
        {
            MaximumFileSizeBytes = maximumFileSizeBytes,
            TimeoutSeconds = timeoutSeconds
        };

        Assert.Throws<InvalidOperationException>(configuration.Validate);
    }

    [Fact(DisplayName = "TD-KAN24-CORE-001 preserves valid linked dimension values")]
    public void Dimension_data_preserves_links_tolerances_and_trimmed_display_text()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        var dimension = new DimensionData(documentId, versionId, 7, DimensionType.Linear, 10.5, -0.1, 0.2, " 10.5 ±0.1 ");

        Assert.Equal(documentId, dimension.DocumentId);
        Assert.Equal(versionId, dimension.DocumentVersionId);
        Assert.Equal(7, dimension.SourceEntityOrdinal);
        Assert.Equal(7, dimension.EntityOrdinal);
        Assert.Equal(DimensionType.Linear, dimension.DimensionType);
        Assert.Equal(10.5, dimension.NominalValue);
        Assert.Equal(-0.1, dimension.LowerTolerance);
        Assert.Equal(0.2, dimension.UpperTolerance);
        Assert.Equal("10.5 ±0.1", dimension.DisplayText);
        Assert.Equal(dimension.DisplayText, dimension.DimensionText);
    }

    [Fact(DisplayName = "TD-KAN24-CORE-001 rejects invalid dimension associations and values")]
    public void Dimension_data_rejects_invalid_associations_and_values()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new DimensionData(Guid.Empty, versionId, 0, DimensionType.Linear, 1));
        Assert.Throws<ArgumentException>(() => new DimensionData(documentId, Guid.Empty, 0, DimensionType.Linear, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionData(documentId, versionId, -1, DimensionType.Linear, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionData(documentId, versionId, 0, (DimensionType)999, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionData(documentId, versionId, 0, DimensionType.Linear, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionData(documentId, versionId, 0, DimensionType.Linear, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DimensionData(documentId, versionId, 0, DimensionType.Linear, 1, double.NegativeInfinity));
        Assert.Throws<ArgumentException>(() => new DimensionData(documentId, versionId, 0, DimensionType.Linear, 1, 0.2, 0.1));
    }

    [Fact(DisplayName = "TD-KAN24-CORE-001 appends verification and dimension audit enum values")]
    public void Existing_persisted_enum_values_remain_compatible()
    {
        Assert.Equal(0, (int)VerificationStatus.Unknown);
        Assert.Equal(1, (int)VerificationStatus.PendingVerification);
        Assert.Equal(2, (int)VerificationStatus.DimensionsExtracted);
        Assert.Equal(3, (int)VerificationStatus.Error);

        Assert.Equal(1, (int)DocumentAuditAction.Uploaded);
        Assert.Equal(2, (int)DocumentAuditAction.DuplicateDetected);
        Assert.Equal(3, (int)DocumentAuditAction.Overwritten);
        Assert.Equal(4, (int)DocumentAuditAction.Cancelled);
        Assert.Equal(5, (int)DocumentAuditAction.MeasurementsExtracted);
        Assert.Equal(6, (int)DocumentAuditAction.MeasurementExtractionFailed);
        Assert.Equal(7, (int)DocumentAuditAction.InvalidStepFileRejected);
        Assert.Equal(8, (int)DocumentAuditAction.InvalidDxfFileRejected);
        Assert.Equal(9, (int)DocumentAuditAction.DimensionsExtracted);
        Assert.Equal(10, (int)DocumentAuditAction.DimensionsExtractedPartial);
        Assert.Equal(11, (int)DocumentAuditAction.DimensionExtractionFailed);
    }

    [Fact(DisplayName = "TD-KAN24-CORE-001 grants dedicated dimension permissions only to Maker and Checker")]
    public void Dimension_permission_matrix_is_restricted_to_maker_and_checker()
    {
        var authorization = new RoleAuthorization();
        var cases = new[]
        {
            (Role: SystemRoles.Maker, CanUse: true),
            (Role: SystemRoles.Checker, CanUse: true),
            (Role: SystemRoles.Admin, CanUse: false),
            (Role: SystemRoles.Approver, CanUse: false),
            (Role: SystemRoles.Viewer, CanUse: false),
            (Role: "Unknown", CanUse: false)
        };

        foreach (var testCase in cases)
        {
            var user = new AuthenticatedUser(testCase.Role, new[] { testCase.Role });
            Assert.Equal(testCase.CanUse, authorization.IsAllowed(user, RolePermission.TriggerDxfDimensionExtraction));
            Assert.Equal(testCase.CanUse, authorization.IsAllowed(user, RolePermission.ViewDxfDimensionResults));
        }

        Assert.False(authorization.IsAllowed(new AuthenticatedUser(SystemRoles.Checker, new[] { SystemRoles.Checker }), RolePermission.TriggerExtraction));
        Assert.False(authorization.IsAllowed(null, RolePermission.TriggerDxfDimensionExtraction));
        Assert.False(authorization.IsAllowed(null, RolePermission.ViewDxfDimensionResults));
    }
}
