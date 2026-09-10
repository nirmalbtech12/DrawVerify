using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class GdtResult
{
    public const string CurrentRuleVersion = "1.0.0";
    public const string UnsupportedReason = "Unsupported GD&T control type";
    public const string MissingToleranceReason = "Missing tolerance value";
    public const string MissingDatumReason = "Datum reference not found";
    public const string AmbiguousAssociationReason = "Feature association ambiguous";
    public const string UnsupportedSyntaxReason = "Syntax or modifier not supported";
    public const string ParsingReason = "Syntax or modifier not supported";
    public Guid GdtResultId { get; init; } = Guid.NewGuid();
    public Guid VerificationSessionId { get; init; }
    public string Identifier { get; init; } = string.Empty;
    public GdtControlType ControlType { get; init; }
    public string? Symbol { get; init; }
    public string? ToleranceValue { get; init; }
    public string? Modifiers { get; init; }
    public IReadOnlyList<string> DatumReferences { get; init; } = Array.Empty<string>();
    public string SourceDocument { get; init; } = string.Empty;
    public string? SourceDocumentVersion { get; init; }
    public string? SourceLocation { get; init; }
    public string? RawText { get; init; }
    public string? EntityHandle { get; init; }
    public string? Layer { get; init; }
    public string? BlockName { get; init; }
    public string? ViewName { get; init; }
    public string RuleVersion { get; init; } = CurrentRuleVersion;
    public string? ControlledFeature { get; init; }
    public string? NominalBasicLocation { get; init; }
    public string? ToleranceZoneType { get; init; }
    public string? ToleranceZoneDiameter { get; init; }
    public string? NormalizedModifiers { get; init; }
    public string? DatumScheme { get; init; }
    public string? ComparisonCriteria { get; init; }
    public string? SourcePairId { get; init; }
    public string? ComparisonDetails { get; init; }
    public bool HasCompositeFrame { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public VerificationStatus Status { get; init; } = VerificationStatus.NotVerified;
    public string? Reason { get; init; }
    public string? ReviewComment { get; init; }
    public bool RequiresEngineeringReview { get; init; }
    public DateTimeOffset? ReviewedUtc { get; init; }
    public string? ReviewDisposition { get; init; }

    public static GdtResult Unsupported(string identifier, GdtControlType controlType = GdtControlType.Unknown,
        string? symbol = null, string? sourceDocument = null, string? sourceLocation = null)
    {
        if (string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("A GD&T identifier is required.", nameof(identifier));
        if (!Enum.IsDefined(controlType)) throw new ArgumentException("The GD&T control type is invalid.", nameof(controlType));
        if (string.IsNullOrWhiteSpace(sourceDocument)) throw new ArgumentException("A source document is required.", nameof(sourceDocument));
        return new GdtResult { Identifier = identifier.Trim(), ControlType = controlType, Symbol = symbol, SourceDocument = sourceDocument.Trim(), SourceLocation = sourceLocation, Status = VerificationStatus.CannotValidate, Reason = UnsupportedReason };
    }

    public GdtResult WithReview(string? comment, bool requiresEngineeringReview, DateTimeOffset? reviewedUtc = null)
    {
        if (comment is not null && string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("A review comment cannot be empty.", nameof(comment));
        return withReview(comment, requiresEngineeringReview, reviewedUtc);
    }

    private GdtResult withReview(string? comment, bool requiresEngineeringReview, DateTimeOffset? reviewedUtc) => new()
    {
        GdtResultId = GdtResultId, VerificationSessionId = VerificationSessionId, Identifier = Identifier, ControlType = ControlType,
        Symbol = Symbol, ToleranceValue = ToleranceValue, Modifiers = Modifiers, DatumReferences = DatumReferences,
        SourceDocument = SourceDocument, SourceDocumentVersion = SourceDocumentVersion, SourceLocation = SourceLocation, RawText = RawText,
        EntityHandle = EntityHandle, Layer = Layer, BlockName = BlockName, ViewName = ViewName, RuleVersion = RuleVersion,
        ControlledFeature = ControlledFeature, NominalBasicLocation = NominalBasicLocation, ToleranceZoneType = ToleranceZoneType,
        ToleranceZoneDiameter = ToleranceZoneDiameter, NormalizedModifiers = NormalizedModifiers, DatumScheme = DatumScheme,
        ComparisonCriteria = ComparisonCriteria, SourcePairId = SourcePairId, ComparisonDetails = ComparisonDetails,
        HasCompositeFrame = HasCompositeFrame, Diagnostics = Diagnostics, Status = Status, Reason = Reason,
        ReviewComment = comment?.Trim(), RequiresEngineeringReview = requiresEngineeringReview, ReviewedUtc = reviewedUtc ?? DateTimeOffset.UtcNow, ReviewDisposition = ReviewDisposition
    };

    public GdtResult WithDisposition(string disposition)
    {
        if (string.IsNullOrWhiteSpace(disposition)) throw new ArgumentException("A GD&T disposition is required.", nameof(disposition));
        return new GdtResult
        {
            GdtResultId = GdtResultId, VerificationSessionId = VerificationSessionId, Identifier = Identifier, ControlType = ControlType, Symbol = Symbol, ToleranceValue = ToleranceValue, Modifiers = Modifiers, DatumReferences = DatumReferences, SourceDocument = SourceDocument, SourceDocumentVersion = SourceDocumentVersion, SourceLocation = SourceLocation, RawText = RawText, EntityHandle = EntityHandle, Layer = Layer, BlockName = BlockName, ViewName = ViewName, RuleVersion = RuleVersion, ControlledFeature = ControlledFeature, NominalBasicLocation = NominalBasicLocation, ToleranceZoneType = ToleranceZoneType, ToleranceZoneDiameter = ToleranceZoneDiameter, NormalizedModifiers = NormalizedModifiers, DatumScheme = DatumScheme, ComparisonCriteria = ComparisonCriteria, SourcePairId = SourcePairId, ComparisonDetails = ComparisonDetails, HasCompositeFrame = HasCompositeFrame, Diagnostics = Diagnostics, Status = Status, Reason = Reason, ReviewComment = ReviewComment, RequiresEngineeringReview = RequiresEngineeringReview, ReviewedUtc = DateTimeOffset.UtcNow, ReviewDisposition = disposition.Trim()
        };
    }
}
