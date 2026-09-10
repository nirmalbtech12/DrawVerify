using DrawVerify.Core.Enums;

namespace DrawVerify.Geometry.Models;

public sealed record GdtAnnotationData(
    string Identifier,
    GdtControlType ControlType,
    string? Symbol,
    string? ToleranceValue,
    string? Modifiers,
    IReadOnlyList<string> DatumReferences,
    string SourceDocument,
    string? SourceLocation,
    string? RawText,
    int SourceOrdinal = 0,
    string? EntityHandle = null,
    string? Layer = null,
    string? BlockName = null,
    string? ViewName = null,
    IReadOnlyList<string>? Diagnostics = null,
    string? ControlledFeature = null,
    string? NominalBasicLocation = null,
    string? ToleranceZoneType = null,
    string? ToleranceZoneDiameter = null,
    string? NormalizedModifiers = null,
    bool HasCompositeFrame = false,
    string? DatumScheme = null,
    string? ComparisonCriteria = null,
    string? SourcePairId = null,
    string? SourceDocumentVersion = null,
    string? ExtractionErrorCategory = null,
    bool UnsupportedSyntax = false);
