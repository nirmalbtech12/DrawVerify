using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

/// <summary>
/// A dimension extracted from one standard DXF DIMENSION entity.
/// </summary>
public sealed record DimensionData
{
    public DimensionData(
        Guid documentId,
        Guid documentVersionId,
        int sourceEntityOrdinal,
        DimensionType dimensionType,
        double nominalValue,
        double? lowerTolerance = null,
        double? upperTolerance = null,
        string? displayText = null)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("A dimension must reference a document.", nameof(documentId));
        if (documentVersionId == Guid.Empty)
            throw new ArgumentException("A dimension must reference a document version.", nameof(documentVersionId));
        if (sourceEntityOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceEntityOrdinal));
        if (!Enum.IsDefined(dimensionType))
            throw new ArgumentOutOfRangeException(nameof(dimensionType));
        if (!double.IsFinite(nominalValue))
            throw new ArgumentOutOfRangeException(nameof(nominalValue));
        if (lowerTolerance is not null && !double.IsFinite(lowerTolerance.Value))
            throw new ArgumentOutOfRangeException(nameof(lowerTolerance));
        if (upperTolerance is not null && !double.IsFinite(upperTolerance.Value))
            throw new ArgumentOutOfRangeException(nameof(upperTolerance));
        if (lowerTolerance is not null && upperTolerance is not null && lowerTolerance > upperTolerance)
            throw new ArgumentException("The lower tolerance cannot exceed the upper tolerance.");

        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        SourceEntityOrdinal = sourceEntityOrdinal;
        DimensionType = dimensionType;
        NominalValue = nominalValue;
        LowerTolerance = lowerTolerance;
        UpperTolerance = upperTolerance;
        DisplayText = string.IsNullOrWhiteSpace(displayText) ? null : displayText.Trim();
    }

    public Guid DocumentId { get; }
    public Guid DocumentVersionId { get; }
    public int SourceEntityOrdinal { get; }
    public int EntityOrdinal => SourceEntityOrdinal;
    public DimensionType DimensionType { get; }
    public double NominalValue { get; }
    public double? LowerTolerance { get; }
    public double? UpperTolerance { get; }
    public string? DisplayText { get; }
    public string? DimensionText => DisplayText;
}
