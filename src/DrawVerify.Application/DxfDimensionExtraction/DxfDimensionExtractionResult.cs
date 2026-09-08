using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

public sealed class DxfDimensionExtractionResult
{
    public DxfDimensionExtractionResult(
        IReadOnlyList<DimensionData>? dimensions = null,
        IReadOnlyList<string>? warnings = null,
        string? errorMessage = null)
    {
        Dimensions = dimensions ?? Array.Empty<DimensionData>();
        Warnings = warnings ?? Array.Empty<string>();
        ErrorMessage = errorMessage;
    }

    public IReadOnlyList<DimensionData> Dimensions { get; }
    public IReadOnlyList<string> Warnings { get; }
    public string? ErrorMessage { get; }
    public string? FatalError => ErrorMessage;
    public bool Succeeded => ErrorMessage is null;
    public bool IsSuccess => Succeeded;
    public bool IsPartial => Succeeded && Dimensions.Count > 0 && Warnings.Count > 0;
    public VerificationStatus VerificationStatus => Succeeded ? VerificationStatus.DimensionsExtracted : VerificationStatus.Error;
    public Document? Document { get; init; }
    public string Message { get; init; } = string.Empty;

    public static DxfDimensionExtractionResult Success(
        IReadOnlyList<DimensionData> dimensions,
        IReadOnlyList<string>? warnings = null) => new(dimensions, warnings);

    public static DxfDimensionExtractionResult Failure(string message, IReadOnlyList<string>? warnings = null) =>
        new(Array.Empty<DimensionData>(), warnings, message);
}
