namespace DrawVerify.Core.Configuration;

public sealed class DimensionMatchingConfiguration
{
    public long MaximumStepFileSizeBytes { get; init; } = 52_428_800;
    public int TimeoutSeconds { get; init; } = 60;
    public double AbsoluteTolerance { get; init; } = 0.01;
    public double ToleranceThresholdMm { get; init; } = 0.1;
    public double AngularToleranceDegrees { get; init; } = 1.0;
    public int ComparisonTimeoutSeconds { get; init; } = 30;
    public double RelativeTolerance { get; init; } = 0.001;
    public double HighConfidenceThreshold { get; init; } = 0.95;
    public double MediumConfidenceThreshold { get; init; } = 0.75;
    public string WorkerPath { get; init; } = string.Empty;
    public void Validate()
    {
        if (MaximumStepFileSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumStepFileSizeBytes));
        if (TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
        if (!double.IsFinite(ToleranceThresholdMm) || ToleranceThresholdMm < 0) throw new ArgumentOutOfRangeException(nameof(ToleranceThresholdMm));
        if (!double.IsFinite(AngularToleranceDegrees) || AngularToleranceDegrees < 0) throw new ArgumentOutOfRangeException(nameof(AngularToleranceDegrees));
        if (ComparisonTimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(ComparisonTimeoutSeconds));
        if (!double.IsFinite(AbsoluteTolerance) || AbsoluteTolerance <= 0) throw new ArgumentOutOfRangeException(nameof(AbsoluteTolerance));
        if (!double.IsFinite(RelativeTolerance) || RelativeTolerance <= 0) throw new ArgumentOutOfRangeException(nameof(RelativeTolerance));
        if (!double.IsFinite(HighConfidenceThreshold) || !double.IsFinite(MediumConfidenceThreshold) || HighConfidenceThreshold <= MediumConfidenceThreshold || MediumConfidenceThreshold <= 0 || HighConfidenceThreshold > 1) throw new ArgumentException("Confidence thresholds must be finite, ordered, and within (0,1].");
    }
}
