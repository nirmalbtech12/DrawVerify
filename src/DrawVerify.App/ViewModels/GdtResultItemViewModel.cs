using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class GdtResultItemViewModel
{
    public GdtResultItemViewModel(GdtResult result) => Result = result ?? throw new ArgumentNullException(nameof(result));
    public GdtResult Result { get; }
    public string StatusLabel => Result.Status switch { VerificationStatus.NotVerified => "Not Verified", VerificationStatus.CannotValidate => "Cannot Validate / Manual Review Required", _ => Result.Status.ToString() };
    public string ControlType => Result.ControlType.ToString();
    public string SourceDocument => Result.SourceDocument;
    public string SourceLocation => Result.SourceLocation ?? "Unavailable";
    public string Disposition => Result.ReviewDisposition ?? "Open";
    public string RuleVersion => Result.RuleVersion;
    public string ComparisonDetails => Result.ComparisonDetails ?? Result.Reason ?? string.Empty;
    public string ControlledFeature => Result.ControlledFeature ?? "Unavailable";
    public string NominalBasicLocation => Result.NominalBasicLocation ?? "Unavailable";
    public string ToleranceZoneType => Result.ToleranceZoneType ?? "Unavailable";
    public string ToleranceZoneDiameter => Result.ToleranceZoneDiameter ?? "Unavailable";
    public string DatumScheme => Result.DatumScheme ?? "Unavailable";
    public string ComparisonCriteria => Result.ComparisonCriteria ?? "Unavailable";
    public string Diagnostics => Result.Diagnostics.Count == 0 ? "None" : string.Join("; ", Result.Diagnostics);
    public string OutcomeExplanation => Result.Reason ?? Result.ComparisonDetails ?? Result.Status switch
    {
        VerificationStatus.Passed => "Validation passed.",
        VerificationStatus.Failed => "Validation failed.",
        VerificationStatus.Mismatched => "Compared GD&T fields differ.",
        _ => "Validation could not be completed."
    };
    public string ControlTypeOrSymbol => Result.ControlType == GdtControlType.Unknown ? Result.Symbol ?? "Unavailable" : Result.Symbol is null ? Result.ControlType.ToString() : $"{Result.ControlType} ({Result.Symbol})";
    public string Tolerance => Result.ToleranceValue ?? "Unavailable";
    public string Modifiers => Result.NormalizedModifiers ?? Result.Modifiers ?? "Unavailable";
    public string Datums => Result.DatumReferences.Count == 0 ? "Unavailable" : string.Join(", ", Result.DatumReferences);
    public string Source => string.IsNullOrWhiteSpace(Result.SourceLocation) ? Result.SourceDocument : $"{Result.SourceDocument} @ {Result.SourceLocation}";
    public string Reason => Result.Reason ?? string.Empty;
    public string Comment => Result.ReviewComment ?? string.Empty;
    public bool RequiresEngineeringReview => Result.RequiresEngineeringReview;
}
