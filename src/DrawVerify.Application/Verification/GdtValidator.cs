namespace DrawVerify.Application.Verification;

using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using DrawVerify.Application.Verification.GdtRules;

/// <summary>
/// Validates GD&T callouts against the documented MVP GD&T subset.
/// </summary>
public sealed class GdtValidator
{
    private readonly IReadOnlySet<GdtControlType> _supported;
    private readonly IReadOnlyDictionary<GdtControlType, Func<GdtResult, VerificationStatus>> _rules;
    public GdtValidator(IEnumerable<GdtControlType>? supportedControls = null)
    {
        _supported = (supportedControls ?? new[] { GdtControlType.Position, GdtControlType.Flatness, GdtControlType.Parallelism, GdtControlType.Perpendicularity }).ToHashSet();
        if (_supported.Contains(GdtControlType.Unknown)) throw new ArgumentException("The supported GD&T subset cannot contain Unknown.", nameof(supportedControls));
        _rules = new Dictionary<GdtControlType, Func<GdtResult, VerificationStatus>>
        {
            [GdtControlType.Position] = new PositionRule().Evaluate,
            [GdtControlType.Flatness] = new FlatnessRule().Evaluate,
            [GdtControlType.Parallelism] = new ParallelismRule().Evaluate,
            [GdtControlType.Perpendicularity] = new PerpendicularityRule().Evaluate
        };
    }
    public static GdtValidator FromSubsetFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A GD&T subset path is required.", nameof(path));
        using var stream = File.OpenRead(path); using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("gdtSubset", out var subset) || !subset.TryGetProperty("version", out var version) || version.GetInt32() != 1) throw new InvalidDataException("The GD&T subset is missing or has an unsupported schema version.");
        if (!subset.TryGetProperty("controls", out var controls) || controls.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The GD&T subset controls collection is missing.");
        var parsed = controls.EnumerateArray().Select(item => ParseType(item.GetProperty("type").GetString())).ToArray();
        if (parsed.Length != parsed.Distinct().Count()) throw new InvalidDataException("The GD&T subset contains duplicate controls.");
        return new GdtValidator(parsed);
    }
    public static GdtValidator FromConfiguredDefault(string? baseDirectory = null) => FromSubsetFile(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "config", "gdt-subset.default.json"));
    public IReadOnlyCollection<GdtControlType> SupportedControls => _supported;
    public IReadOnlyList<GdtResult> Validate(IEnumerable<GdtAnnotationData> annotations, Guid verificationSessionId = default)
    { ArgumentNullException.ThrowIfNull(annotations); return annotations.Select(annotation => Validate(annotation, verificationSessionId)).ToArray(); }
    public GdtResult Validate(GdtAnnotationData annotation, Guid verificationSessionId = default)
    {
        ArgumentNullException.ThrowIfNull(annotation); if (string.IsNullOrWhiteSpace(annotation.Identifier)) throw new ArgumentException("A GD&T annotation identifier is required.", nameof(annotation));
        var supported = annotation.ControlType != GdtControlType.Unknown && _supported.Contains(annotation.ControlType);
        var result = new GdtResult { GdtResultId = CreateStableId(annotation.Identifier), VerificationSessionId = verificationSessionId, Identifier = annotation.Identifier, ControlType = annotation.ControlType, Symbol = annotation.Symbol, ToleranceValue = annotation.ToleranceValue, Modifiers = annotation.Modifiers, DatumReferences = annotation.DatumReferences ?? Array.Empty<string>(), SourceDocument = annotation.SourceDocument, SourceLocation = annotation.SourceLocation, RawText = annotation.RawText, EntityHandle = annotation.EntityHandle, Layer = annotation.Layer, BlockName = annotation.BlockName, ViewName = annotation.ViewName, Diagnostics = annotation.Diagnostics ?? Array.Empty<string>(), Status = VerificationStatus.NotVerified, Reason = GdtResult.UnsupportedReason };
        if (!supported) return result;
        return new GdtResult { GdtResultId = result.GdtResultId, VerificationSessionId = result.VerificationSessionId, Identifier = result.Identifier, ControlType = result.ControlType, Symbol = result.Symbol, ToleranceValue = result.ToleranceValue, Modifiers = result.Modifiers, DatumReferences = result.DatumReferences, SourceDocument = result.SourceDocument, SourceLocation = result.SourceLocation, RawText = result.RawText, EntityHandle = result.EntityHandle, Layer = result.Layer, BlockName = result.BlockName, ViewName = result.ViewName, Diagnostics = result.Diagnostics, Status = _rules[annotation.ControlType](result), Reason = null };
    }
    private static GdtControlType ParseType(string? value) => Enum.TryParse<GdtControlType>(value, true, out var type) && type != GdtControlType.Unknown ? type : throw new InvalidDataException($"Unsupported GD&T control type '{value}'.");
    private static Guid CreateStableId(string identifier) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(identifier)).AsSpan(0, 16));
}
