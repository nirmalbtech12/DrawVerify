namespace DrawVerify.Geometry;

/// <summary>Validates uploaded STEP content through the configured model reader.</summary>
public interface IStepModelValidator
{
    Task<StepModelValidationResult> ValidateAsync(Stream content, CancellationToken cancellationToken = default);
}

public sealed record StepModelValidationResult(bool IsValid, string? ErrorCode = null, string? ErrorMessage = null);
