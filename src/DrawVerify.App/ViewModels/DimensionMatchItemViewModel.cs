using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.Globalization;
using System.Windows.Media;
using System.Windows;

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
    public string Layer => Match.DxfLayer ?? "-";
    public string PreviewAnchor => Match.PreviewAnchor ?? "-";
    public string Comment => Match.Review?.Comment ?? "";
    public string ResolutionStatus => Match.Review?.Status.ToString() ?? "Open";
    public bool IsUnmatched => Match.Status == DimensionMatchStatus.Unmatched;
    public string MeasuredValue => FormatValue(Match.MeasuredValue);
    public string DeltaUnit => Match.DeltaUnit;
    public string SignedDelta => FormatDelta(Match.SignedDelta, Match.DeltaUnit);
    public string AbsoluteDelta => FormatDelta(Match.AbsoluteDelta, Match.DeltaUnit, includeSign: false);
    public double? DeltaMagnitude => Match.DeltaMagnitude;
    public bool HasDelta => Match.Status == DimensionMatchStatus.Mismatched && Match.DeltaMagnitude is not null;
    public bool IsSignificantDelta => HasDelta && Match.EffectiveLowerTolerance is double lower && Match.EffectiveUpperTolerance is double upper && Match.DeltaMagnitude > Math.Max(Math.Abs(lower), Math.Abs(upper));
    public Brush DeltaBrush => !HasDelta ? Brushes.Gray : IsSignificantDelta ? Brushes.Red : Brushes.DarkOrange;
    public FontWeight DeltaFontWeight => IsSignificantDelta ? FontWeights.Bold : FontWeights.Normal;
    public string Tolerance => Match.EffectiveLowerTolerance is null || Match.EffectiveUpperTolerance is null ? "-" : $"{FormatValue(Match.EffectiveLowerTolerance)} / +{FormatValue(Match.EffectiveUpperTolerance)} {Match.ToleranceUnit}";
    public string ToleranceSource => Match.ToleranceSource?.ToString() ?? "-";
    public bool IsMatched => Match.Status == DimensionMatchStatus.Matched;
    public string StatusColor => Match.Status switch
    {
        DimensionMatchStatus.Matched => "Green",
        DimensionMatchStatus.Mismatched => "Red",
        DimensionMatchStatus.Unmatched => "Gray",
        _ => "Gray"
    };
    public Brush StatusBrush => Match.Status switch
    {
        DimensionMatchStatus.Matched => Brushes.Green,
        DimensionMatchStatus.Mismatched => Brushes.Red,
        DimensionMatchStatus.Unmatched => Brushes.Gray,
        _ => Brushes.Gray
    };
    private static string FormatValue(double? value) => value is null ? "-" : value.Value.ToString("0.############", CultureInfo.InvariantCulture);
    private static string FormatDelta(double? value, string unit, bool includeSign = true) => value is not double number || !double.IsFinite(number) ? "-" : $"Δ = {(includeSign ? number.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : number.ToString("0.000", CultureInfo.InvariantCulture))} {unit}";
}
