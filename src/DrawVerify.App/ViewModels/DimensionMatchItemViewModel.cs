using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;
public sealed class DimensionMatchItemViewModel
{
    public DimensionMatchItemViewModel(DimensionMatch match) { Match = match; }
    public DimensionMatch Match { get; }
    public string StatusLabel => Match.Status switch { DimensionMatchStatus.Matched when Match.RequiresManualReview => "Manual review" , DimensionMatchStatus.Matched => "Matched", DimensionMatchStatus.Unmatched => "Unmatched", DimensionMatchStatus.MissingDimension => "Missing Dimension", _ => "Unknown" };
    public string ConfidenceLabel => Match.Confidence?.ToString() ?? "-";
    public string GeometryReference => Match.StepGeometryReference ?? "-";
    public string Reason => Match.ReasonCode ?? string.Empty;
    public string ReasonExplanation => Match.ReasonExplanation ?? string.Empty;
    public string DimensionType => Match.DimensionType?.ToString() ?? "-";
    public string NominalValue => Match.NominalValue?.ToString("G17") ?? "-";
    public string DimensionText => Match.DimensionText ?? "-";
    public bool IsUnmatched => Match.Status == DimensionMatchStatus.Unmatched;
}
