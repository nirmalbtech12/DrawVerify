namespace DrawVerify.Application.Verification;

using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;
using DrawVerify.Application.Verification.GdtRules;

public sealed class GdtValidator
{
    private static readonly GdtControlType[] DefaultControls = [GdtControlType.Position, GdtControlType.Flatness, GdtControlType.Parallelism, GdtControlType.Perpendicularity];
    private readonly IReadOnlySet<GdtControlType> _supported;
    private readonly IReadOnlyDictionary<GdtControlType, Func<GdtAnnotationData, GdtAnnotationData, IReadOnlyList<string>>> _rules;
    public string RuleVersion { get; }
    public GdtValidator(IEnumerable<GdtControlType>? supportedControls = null, string ruleVersion = GdtResult.CurrentRuleVersion)
    {
        if (string.IsNullOrWhiteSpace(ruleVersion) || !Version.TryParse(ruleVersion, out _)) throw new ArgumentException("The GD&T rule version is invalid.", nameof(ruleVersion));
        _supported = (supportedControls ?? DefaultControls).ToHashSet();
        if (_supported.Any(type => !DefaultControls.Contains(type))) throw new ArgumentException("The GD&T subset contains an unsupported control.", nameof(supportedControls));
        RuleVersion = ruleVersion.Trim();
        _rules = new Dictionary<GdtControlType, Func<GdtAnnotationData, GdtAnnotationData, IReadOnlyList<string>>>
        {
            [GdtControlType.Position] = new PositionRule().Compare,
            [GdtControlType.Flatness] = new FlatnessRule().Compare,
            [GdtControlType.Parallelism] = new ParallelismRule().Compare,
            [GdtControlType.Perpendicularity] = new PerpendicularityRule().Compare
        };
    }
    public static GdtValidator FromSubsetFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A GD&T subset path is required.", nameof(path));
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var subset = document.RootElement.GetProperty("gdtSubset");
        if (subset.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("The GD&T subset has an unsupported schema version.");
        var parsed = subset.GetProperty("controls").EnumerateArray().Select(x => ParseType(x.GetProperty("type").GetString())).ToArray();
        if (parsed.Length != parsed.Distinct().Count()) throw new InvalidDataException("The GD&T subset contains duplicate controls.");
        var ruleVersion = subset.TryGetProperty("ruleVersion", out var versionProperty) ? versionProperty.GetString() : GdtResult.CurrentRuleVersion;
        return new GdtValidator(parsed, ruleVersion ?? throw new InvalidDataException("The GD&T subset rule version is missing."));
    }
    public static GdtValidator FromConfiguredDefault(string? baseDirectory = null) => FromSubsetFile(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "config", "gdt-subset.default.json"));
    public IReadOnlyCollection<GdtControlType> SupportedControls => _supported;
    public IReadOnlyList<GdtResult> Validate(IEnumerable<GdtAnnotationData> annotations, Guid sessionId = default) => annotations.Select(a => Validate(a, sessionId)).ToArray();
    public IReadOnlyList<GdtResult> Validate(IEnumerable<GdtAnnotationData> step, IEnumerable<GdtAnnotationData> dxf, Guid sessionId = default)
    {
        ArgumentNullException.ThrowIfNull(step); ArgumentNullException.ThrowIfNull(dxf);
        var drawing = dxf.ToArray(); return step.Select(a => ValidatePair(a, drawing, sessionId)).ToArray();
    }
    public GdtResult Validate(GdtAnnotationData annotation, Guid sessionId = default)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (!string.IsNullOrWhiteSpace(annotation.ExtractionErrorCategory)) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.ParsingReason, null);
        if (HasParsingDiagnostic(annotation)) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.ParsingReason, null);
        if (!_supported.Contains(annotation.ControlType)) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.UnsupportedReason, null);
        if (Unsupported(annotation)) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.UnsupportedSyntaxReason, null);
        if (string.IsNullOrWhiteSpace(annotation.ToleranceValue)) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.MissingToleranceReason, null);
        if (annotation.ControlType is GdtControlType.Parallelism or GdtControlType.Perpendicularity && (annotation.DatumReferences ?? Array.Empty<string>()).Count == 0) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.MissingDatumReason, null);
        if (string.IsNullOrWhiteSpace(annotation.ControlledFeature)) return Create(annotation, sessionId, VerificationStatus.CannotValidate, GdtResult.AmbiguousAssociationReason, null);
        return Create(annotation, sessionId, VerificationStatus.NotVerified, "A corresponding STEP/DXF control is required for comparison.", null);
    }
    private GdtResult ValidatePair(GdtAnnotationData step, IReadOnlyList<GdtAnnotationData> dxf, Guid sessionId)
    {
        if (string.IsNullOrWhiteSpace(step.Identifier)) throw new ArgumentException("A GD&T annotation identifier is required.", nameof(step));
        var result = Create(step, sessionId, VerificationStatus.NotVerified, null, null);
        if (!string.IsNullOrWhiteSpace(step.ExtractionErrorCategory)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.ParsingReason, null);
        if (!_supported.Contains(step.ControlType)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.UnsupportedReason, null);
        if (HasParsingDiagnostic(step)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.ParsingReason, null);
        if (Unsupported(step)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.UnsupportedSyntaxReason, null);
        if (!TryFindCounterpart(step, dxf, out var other, out var matchingReason)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.AmbiguousAssociationReason, null);
        if (!string.IsNullOrWhiteSpace(other.ExtractionErrorCategory) || HasParsingDiagnostic(other)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.ParsingReason, null);
        if (Unsupported(other)) return result.Copy(VerificationStatus.CannotValidate, GdtResult.UnsupportedSyntaxReason, null);
        var missing = Missing(step, other);
        if (missing is not null) return result.Copy(VerificationStatus.CannotValidate, missing, null);
        var differences = _rules[step.ControlType](step, other).ToArray();
        var details = differences.Length == 0 ? "All applicable GD&T fields agree." : $"Mismatched fields: {string.Join(", ", differences)}.";
        if (differences.Length > 0) return result.Copy(VerificationStatus.Mismatched, details, details);
        var criterion = EvaluateCriterion(step.ComparisonCriteria);
        if (criterion == CriterionOutcome.Failed) return result.Copy(VerificationStatus.Failed, "The approved GD&T comparison criterion was violated.", $"{details} Criterion violation: {step.ComparisonCriteria}.");
        if (criterion == CriterionOutcome.NotVerified) return result.Copy(VerificationStatus.NotVerified, "The GD&T comparison criterion could not be evaluated.", $"{details} Criterion: {step.ComparisonCriteria}.");
        return result.Copy(VerificationStatus.Passed, null, $"{details} Criterion: {step.ComparisonCriteria ?? DefaultCriteria(step.ControlType)}.");
    }
    private static bool TryFindCounterpart(GdtAnnotationData step, IReadOnlyList<GdtAnnotationData> dxf, out GdtAnnotationData counterpart, out string reason)
    {
        var sameType = dxf.Where(candidate => candidate.ControlType == step.ControlType).ToArray();
        var identifierMatches = sameType.Where(candidate => string.Equals(step.Identifier, candidate.Identifier, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (identifierMatches.Length > 1)
        {
            counterpart = null!;
            reason = "Ambiguous GD&T control match for the stable identifier.";
            return false;
        }
        if (identifierMatches.Length == 1)
        {
            var identifierMatch = identifierMatches[0];
            var conflictingFallback = sameType.Any(candidate => !ReferenceEquals(candidate, identifierMatch) &&
                !string.Equals(step.Identifier, candidate.Identifier, StringComparison.OrdinalIgnoreCase) &&
                MatchesFallbackAssociation(step, candidate));
            if (conflictingFallback)
            {
                counterpart = null!;
                reason = "Conflicting GD&T identifiers share the same feature or source-pair association.";
                return false;
            }
            counterpart = identifierMatch;
            reason = string.Empty;
            return true;
        }

        var fallbackMatches = sameType.Where(candidate => MatchesFallbackAssociation(step, candidate)).ToArray();
        if (fallbackMatches.Length == 0)
        {
            counterpart = null!;
            reason = "Corresponding DXF control could not be reliably identified by stable identifier, feature, or source-pair association.";
            return false;
        }
        if (fallbackMatches.Length > 1)
        {
            counterpart = null!;
            reason = "Ambiguous GD&T fallback match by controlled feature or source-pair association.";
            return false;
        }
        counterpart = fallbackMatches[0];
        reason = string.Empty;
        return true;
    }
    private static bool MatchesFallbackAssociation(GdtAnnotationData step, GdtAnnotationData candidate)
    {
        var featureMatches = !string.IsNullOrWhiteSpace(step.ControlledFeature) && !string.IsNullOrWhiteSpace(candidate.ControlledFeature) && string.Equals(step.ControlledFeature, candidate.ControlledFeature, StringComparison.OrdinalIgnoreCase);
        var sourcePairMatches = !string.IsNullOrWhiteSpace(step.SourcePairId) && !string.IsNullOrWhiteSpace(candidate.SourcePairId) && string.Equals(step.SourcePairId, candidate.SourcePairId, StringComparison.OrdinalIgnoreCase);
        return featureMatches || sourcePairMatches;
    }
    private GdtResult Create(GdtAnnotationData a, Guid sessionId, VerificationStatus status, string? reason, string? details)
    {
        if (string.IsNullOrWhiteSpace(a.Identifier)) throw new ArgumentException("A GD&T annotation identifier is required.", nameof(a));
        if (string.IsNullOrWhiteSpace(a.SourceDocument)) throw new ArgumentException("A GD&T source document is required.", nameof(a));
        return new()
    {
        GdtResultId = StableId(a.Identifier), VerificationSessionId = sessionId, Identifier = a.Identifier.Trim(), ControlType = a.ControlType, Symbol = a.Symbol,
        ToleranceValue = a.ToleranceValue, Modifiers = a.Modifiers, DatumReferences = a.DatumReferences ?? Array.Empty<string>(), SourceDocument = a.SourceDocument,
        SourceDocumentVersion = a.SourceDocumentVersion, SourceLocation = a.SourceLocation, RawText = a.RawText, EntityHandle = a.EntityHandle, Layer = a.Layer, BlockName = a.BlockName, ViewName = a.ViewName,
        Diagnostics = a.Diagnostics ?? Array.Empty<string>(), RuleVersion = RuleVersion, ControlledFeature = a.ControlledFeature, NominalBasicLocation = a.NominalBasicLocation,
        ToleranceZoneType = a.ToleranceZoneType, ToleranceZoneDiameter = a.ToleranceZoneDiameter, NormalizedModifiers = a.NormalizedModifiers,
        HasCompositeFrame = a.HasCompositeFrame, DatumScheme = a.DatumScheme, ComparisonCriteria = a.ComparisonCriteria ?? DefaultCriteria(a.ControlType), SourcePairId = a.SourcePairId,
        Status = status, Reason = reason, ComparisonDetails = details
    };
    }
    private static bool Unsupported(GdtAnnotationData a) => a.HasCompositeFrame || a.DatumScheme?.Contains("unsupported", StringComparison.OrdinalIgnoreCase) == true || a.Modifiers?.Contains("unsupported", StringComparison.OrdinalIgnoreCase) == true;
    private static bool HasParsingDiagnostic(GdtAnnotationData a) => (a.Diagnostics ?? Array.Empty<string>()).Any(d => d.Contains("parse", StringComparison.OrdinalIgnoreCase) || d.Contains("syntax", StringComparison.OrdinalIgnoreCase) || d.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) || d.Contains("not recognized", StringComparison.OrdinalIgnoreCase) || d.Contains("not extracted", StringComparison.OrdinalIgnoreCase));
    private static string? Missing(GdtAnnotationData a, GdtAnnotationData b)
    {
        if (string.IsNullOrWhiteSpace(a.ToleranceValue) || string.IsNullOrWhiteSpace(b.ToleranceValue)) return GdtResult.MissingToleranceReason;
        if (string.IsNullOrWhiteSpace(a.ControlledFeature) || string.IsNullOrWhiteSpace(b.ControlledFeature)) return GdtResult.AmbiguousAssociationReason;
        if (a.ControlType is GdtControlType.Parallelism or GdtControlType.Perpendicularity && (a.DatumReferences.Count == 0 || b.DatumReferences.Count == 0)) return GdtResult.MissingDatumReason;
        return null;
    }
    private static string DefaultCriteria(GdtControlType type) => type switch
    {
        GdtControlType.Position => "controlled feature/basic location/tolerance zone within position tolerance",
        GdtControlType.Flatness => "controlled surface within two parallel planes",
        GdtControlType.Parallelism => "controlled feature within parallel tolerance zone relative to datum",
        GdtControlType.Perpendicularity => "controlled feature within perpendicular tolerance zone relative to datum",
        _ => "unsupported GD&T criterion"
    };
    private static CriterionOutcome EvaluateCriterion(string? criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion)) return CriterionOutcome.NotSpecified;
        var normalized = criterion.Trim().ToLowerInvariant();
        if (normalized.Contains("outside", StringComparison.Ordinal) || normalized.Contains("exceed", StringComparison.Ordinal) || normalized.Contains("violat", StringComparison.Ordinal) || normalized.Contains("fail", StringComparison.Ordinal) || normalized.Contains("not within", StringComparison.Ordinal) || normalized.Contains("does not meet", StringComparison.Ordinal) || normalized.Contains("false", StringComparison.Ordinal)) return CriterionOutcome.Failed;
        if (normalized.Contains("within", StringComparison.Ordinal) || normalized.Contains("pass", StringComparison.Ordinal) || normalized.Contains("satisf", StringComparison.Ordinal) || normalized.Contains("meet", StringComparison.Ordinal) || normalized.Contains("true", StringComparison.Ordinal)) return CriterionOutcome.Passed;
        return CriterionOutcome.NotVerified;
    }
    private enum CriterionOutcome { NotSpecified, Passed, Failed, NotVerified }
    private static GdtControlType ParseType(string? value) => Enum.TryParse<GdtControlType>(value, true, out var type) && DefaultControls.Contains(type) ? type : throw new InvalidDataException($"Unsupported GD&T control type '{value}'.");
    private static Guid StableId(string id) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(id)).AsSpan(0, 16));
}

file static class GdtResultCopy
{
    public static GdtResult Copy(this GdtResult v, VerificationStatus status, string? reason, string? details) => new()
    {
        GdtResultId=v.GdtResultId, VerificationSessionId=v.VerificationSessionId, Identifier=v.Identifier, ControlType=v.ControlType, Symbol=v.Symbol, ToleranceValue=v.ToleranceValue, Modifiers=v.Modifiers, DatumReferences=v.DatumReferences, SourceDocument=v.SourceDocument, SourceDocumentVersion=v.SourceDocumentVersion, SourceLocation=v.SourceLocation, RawText=v.RawText, EntityHandle=v.EntityHandle, Layer=v.Layer, BlockName=v.BlockName, ViewName=v.ViewName, Diagnostics=v.Diagnostics, RuleVersion=v.RuleVersion, ControlledFeature=v.ControlledFeature, NominalBasicLocation=v.NominalBasicLocation, ToleranceZoneType=v.ToleranceZoneType, ToleranceZoneDiameter=v.ToleranceZoneDiameter, NormalizedModifiers=v.NormalizedModifiers, HasCompositeFrame=v.HasCompositeFrame, DatumScheme=v.DatumScheme, ComparisonCriteria=v.ComparisonCriteria, SourcePairId=v.SourcePairId, Status=status, Reason=reason, ComparisonDetails=details, ReviewComment=v.ReviewComment, RequiresEngineeringReview=v.RequiresEngineeringReview, ReviewedUtc=v.ReviewedUtc, ReviewDisposition=v.ReviewDisposition
    };
}
