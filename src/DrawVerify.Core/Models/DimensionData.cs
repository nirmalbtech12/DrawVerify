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
        string? displayText = null,
        RadialDimensionKind radialDimensionKind = RadialDimensionKind.Unknown,
        string? layer = null,
        string? previewAnchor = null,
        string? blockName = null,
        string? viewName = null,
        double? locationX = null,
        double? locationY = null)
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
        if (locationX is not null && !double.IsFinite(locationX.Value))
            throw new ArgumentOutOfRangeException(nameof(locationX));
        if (locationY is not null && !double.IsFinite(locationY.Value))
            throw new ArgumentOutOfRangeException(nameof(locationY));
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
        RadialKind = radialDimensionKind;
        Layer = string.IsNullOrWhiteSpace(layer) ? null : layer.Trim();
        PreviewAnchor = string.IsNullOrWhiteSpace(previewAnchor) ? null : previewAnchor.Trim();
        BlockName = string.IsNullOrWhiteSpace(blockName) ? null : blockName.Trim();
        ViewName = string.IsNullOrWhiteSpace(viewName) ? null : viewName.Trim();
        LocationX = locationX;
        LocationY = locationY;
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
    public RadialDimensionKind RadialKind { get; }
    public string? Layer { get; }
    public string? PreviewAnchor { get; }
    public string? BlockName { get; }
    public string? ViewName { get; }
    public double? LocationX { get; }
    public double? LocationY { get; }
    public double? X => LocationX;
    public double? Y => LocationY;
}
