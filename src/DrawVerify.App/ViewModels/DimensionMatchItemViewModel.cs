using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Globalization;
using System.Windows.Media;

namespace DrawVerify.App.ViewModels;
public sealed class DimensionMatchItemViewModel
{
    public DimensionMatchItemViewModel(DimensionMatch match) { Match = match; }
    public DimensionMatch Match { get; }
    public string StatusLabel => Match.Status switch { DimensionMatchStatus.Matched when Match.RequiresManualReview => "Manual review" , DimensionMatchStatus.Matched => "Matched", DimensionMatchStatus.Mismatched => "Mismatched", DimensionMatchStatus.Unmatched => "Unmatched", DimensionMatchStatus.MissingDimension => "Missing Dimension", _ => "Unknown" };
    public string ConfidenceLabel => Match.Confidence?.ToString() ?? "-";
    public string GeometryReference => Match.StepGeometryReference ?? "-";
    public string Reason => Match.ReasonCode ?? string.Empty;
    public string ReasonExplanation => Match.ReasonExplanation ?? string.Empty;
    public string DimensionType => Match.DimensionType?.ToString() ?? "-";
    public string NominalValue => FormatValue(Match.NominalValue);
    public string DimensionText => Match.DimensionText ?? "-";
    public bool IsUnmatched => Match.Status == DimensionMatchStatus.Unmatched;
    public string MeasuredValue => FormatValue(Match.MeasuredValue);
    public string Tolerance => Match.EffectiveLowerTolerance is null || Match.EffectiveUpperTolerance is null ? "-" : $"{FormatValue(Match.EffectiveLowerTolerance)} / +{FormatValue(Match.EffectiveUpperTolerance)} {Match.ToleranceUnit}";
    public string ToleranceSource => Match.ToleranceSource?.ToString() ?? "-";
    public bool IsMatched => Match.Status == DimensionMatchStatus.Matched;
    public string StatusColor => IsMatched ? "Green" : Match.Status == DimensionMatchStatus.Mismatched ? "Red" : "Gray";
    public Brush StatusBrush => IsMatched ? Brushes.Green : Match.Status == DimensionMatchStatus.Mismatched ? Brushes.Red : Brushes.Gray;
    private static string FormatValue(double? value) => value is null ? "-" : value.Value.ToString("0.############", CultureInfo.InvariantCulture);
}
