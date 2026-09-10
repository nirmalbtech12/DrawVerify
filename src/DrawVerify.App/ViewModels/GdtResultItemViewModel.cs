using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class GdtResultItemViewModel
{
    public GdtResultItemViewModel(GdtResult result) => Result = result ?? throw new ArgumentNullException(nameof(result));
    public GdtResult Result { get; }
    public string StatusLabel => Result.Status == VerificationStatus.NotVerified ? "Not Verified" : Result.Status.ToString();
    public string RuleVersion => Result.RuleVersion;
    public string ComparisonDetails => Result.ComparisonDetails ?? Result.Reason ?? string.Empty;
    public string ControlTypeOrSymbol => Result.ControlType == GdtControlType.Unknown ? Result.Symbol ?? "Unavailable" : Result.Symbol is null ? Result.ControlType.ToString() : $"{Result.ControlType} ({Result.Symbol})";
    public string Tolerance => Result.ToleranceValue ?? "Unavailable";
    public string Modifiers => Result.NormalizedModifiers ?? Result.Modifiers ?? "Unavailable";
    public string Datums => Result.DatumReferences.Count == 0 ? "Unavailable" : string.Join(", ", Result.DatumReferences);
    public string Source => string.IsNullOrWhiteSpace(Result.SourceLocation) ? Result.SourceDocument : $"{Result.SourceDocument} @ {Result.SourceLocation}";
    public string Reason => Result.Reason ?? string.Empty;
    public string Comment => Result.ReviewComment ?? string.Empty;
    public bool RequiresEngineeringReview => Result.RequiresEngineeringReview;
}
