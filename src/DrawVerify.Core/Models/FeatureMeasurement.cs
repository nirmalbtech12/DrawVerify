namespace DrawVerify.Core.Models;

public sealed record FeatureMeasurement(
    Guid DocumentVersionId,
    string FeatureType,
    IReadOnlyDictionary<string, double> NominalDimensions,
    string Unit,
    double? LowerTolerance = null,
    double? UpperTolerance = null,
    string? Uncertainty = null);
