namespace DrawVerify.Core.Configuration;

public sealed class DxfDimensionExtractionConfiguration
{
    public const long DefaultMaximumFileSizeBytes = 52_428_800;
    public const int DefaultTimeoutSeconds = 30;

    public long MaximumFileSizeBytes { get; init; } = DefaultMaximumFileSizeBytes;
    public long MaxFileSizeBytes => MaximumFileSizeBytes;
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public void Validate()
    {
        if (MaximumFileSizeBytes <= 0)
            throw new InvalidOperationException("DXF dimension extraction maximum file size must be positive.");
        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException("DXF dimension extraction timeout must be positive.");
    }
}
