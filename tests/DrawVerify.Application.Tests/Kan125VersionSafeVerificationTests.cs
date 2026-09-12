using DrawVerify.Application.Verification;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using Xunit;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

namespace DrawVerify.Application.Tests;

public sealed class Kan125VersionSafeVerificationTests
{
    [Fact(DisplayName = "TD-09 passes explicit selected version IDs to matching")]
    public void Matching_uses_selected_versions_and_returns_explicit_classifications()
    {
        var stepVersion = Guid.NewGuid();
        var dxfVersion = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var session = new VerificationSession
        {
            VerificationSessionId = Guid.NewGuid(),
            StepDocumentVersionId = stepVersion,
            DxfDocumentVersionId = dxfVersion
        };
        var dimensions = new[]
        {
            new CoreDimensionData(documentId, dxfVersion, 1, DimensionType.Linear, 10),
            new CoreDimensionData(documentId, dxfVersion, 2, DimensionType.Linear, 10),
            new CoreDimensionData(documentId, dxfVersion, 3, DimensionType.Linear, 99)
        };
        var features = new[]
        {
            new GeometryFeatureData { Reference = "matched", GeometryType = "Line", Length = 10 },
            new GeometryFeatureData { Reference = "mismatched", GeometryType = "Line", Length = 10.5 },
            new GeometryFeatureData { Reference = "extra", GeometryType = "Hole", Diameter = 20 }
        };

        var results = new DimensionMatchingAlgorithm(new DimensionMatchingConfiguration()).Match(session, dimensions, features);

        Assert.Contains(results, item => item.Status == DimensionMatchStatus.Matched && item.StepDocumentVersionId == stepVersion && item.DxfDocumentVersionId == dxfVersion);
        Assert.Contains(results, item => item.Status == DimensionMatchStatus.Mismatched && item.RequiresManualReview && item.StepDocumentVersionId == stepVersion && item.DxfDocumentVersionId == dxfVersion);
        Assert.Contains(results, item => item.Status == DimensionMatchStatus.Unmatched && item.RequiresManualReview && item.DxfDocumentVersionId == dxfVersion);
        Assert.Contains(results, item => item.Status == DimensionMatchStatus.ExtraDimension && item.RequiresManualReview && item.StepDocumentVersionId == stepVersion && item.DxfDocumentVersionId == dxfVersion);
    }

    [Fact(DisplayName = "TD-09 preserves all result classifications")]
    public void Result_model_exposes_classifications_and_review_flag()
    {
        var sessionId = Guid.NewGuid();
        var stepVersion = Guid.NewGuid();
        var dxfVersion = Guid.NewGuid();
        var results = new[]
        {
            new DimensionMatch { VerificationSessionId = sessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = DimensionMatchStatus.Matched },
            new DimensionMatch { VerificationSessionId = sessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = DimensionMatchStatus.Mismatched, RequiresManualReview = true, ReasonCode = "OutsideTolerance" },
            new DimensionMatch { VerificationSessionId = sessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = DimensionMatchStatus.MissingDimension, RequiresManualReview = true },
            new DimensionMatch { VerificationSessionId = sessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = DimensionMatchStatus.ExtraDimension, RequiresManualReview = true },
            new DimensionMatch { VerificationSessionId = sessionId, StepDocumentVersionId = stepVersion, DxfDocumentVersionId = dxfVersion, Status = DimensionMatchStatus.Unmatched, RequiresManualReview = true, ReasonCode = "NoCorrespondingDimension" }
        };

        Assert.Equal(5, results.Select(item => item.Status).Distinct().Count());
        Assert.All(results, item =>
        {
            Assert.Equal(stepVersion, item.StepDocumentVersionId);
            Assert.Equal(dxfVersion, item.DxfDocumentVersionId);
        });
        Assert.Contains(results, item => item.RequiresManualReview && item.ReasonCode == "OutsideTolerance");
    }

}
