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
    IReadOnlyList<string>? Diagnostics = null);
