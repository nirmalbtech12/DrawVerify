namespace DrawVerify.Core.Models;

public sealed class VerificationReport
{
    public IReadOnlyList<DimensionMatch> DimensionMatches { get; init; } = Array.Empty<DimensionMatch>();
    public IReadOnlyList<DimensionMatch> UnmatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Unmatched).ToArray();
    public IReadOnlyList<DimensionMatch> MismatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Mismatched).ToArray();
    public string ToMarkdown()
    {
        var lines = new List<string> { "# Verification Report", string.Empty, "## Mismatched Dimensions with Delta", string.Empty };
        foreach (var match in MismatchedDimensions)
            lines.Add($"- Reference: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | DXF nominal: {Format(match.NominalValue)} {match.DeltaUnit} | STEP measured: {Format(match.MeasuredValue)} {match.DeltaUnit} | Absolute delta: {Format(match.AbsoluteDelta)} {match.DeltaUnit} | Signed delta (DXF − STEP): {FormatSigned(match.SignedDelta)} {match.DeltaUnit}");
        lines.Add(string.Empty);
        lines.Add("## Unmatched Dimensions – Requires Manual Review");
        lines.Add(string.Empty);
        foreach (var match in UnmatchedDimensions)
            lines.Add($"- Reference: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | Nominal value: {match.NominalValue?.ToString("G17") ?? "-"} | Reason: {match.ReasonCode ?? "-"} ({match.ReasonExplanation ?? "-"})");
        return string.Join(Environment.NewLine, lines);
    }
    private static string Format(double? value) => value is double number && double.IsFinite(number) ? number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-";
    private static string FormatSigned(double? value) => value is double number && double.IsFinite(number) ? $"{number:+0.###;-0.###;0}" : "-";
}
