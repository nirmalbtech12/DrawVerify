namespace DrawVerify.Core.Models;

public sealed class VerificationReport
{
    public IReadOnlyList<DimensionMatch> DimensionMatches { get; init; } = Array.Empty<DimensionMatch>();
    public IReadOnlyList<DimensionMatch> UnmatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Unmatched).ToArray();
    public IReadOnlyList<DimensionMatch> MismatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Mismatched).ToArray();
    public string ToMarkdown()
    {
        var lines = new List<string> { "# Verification Report", string.Empty, "## Dimension Status Summary", string.Empty };
        foreach (var type in Enum.GetValues<Enums.DimensionType>())
        {
            var typeMatches = DimensionMatches.Where(match => match.DimensionType == type).ToArray();
            lines.Add($"- {type}: {typeMatches.Count(match => match.Status == Enums.DimensionMatchStatus.Matched)} Matched, {typeMatches.Count(match => match.Status == Enums.DimensionMatchStatus.Mismatched)} Mismatched, {typeMatches.Count(match => match.Status == Enums.DimensionMatchStatus.Unmatched)} Unmatched");
        }
        lines.Add(string.Empty);
        lines.Add("## Dimension Status Details");
        lines.Add(string.Empty);
        foreach (var match in DimensionMatches)
            lines.Add($"- Dimension: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | Status: {FormatStatus(match.Status)}");
        lines.Add(string.Empty);
        lines.Add("## Mismatched Dimensions with Delta");
        lines.Add(string.Empty);
        foreach (var match in MismatchedDimensions)
            lines.Add($"- Reference: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | DXF nominal: {Format(match.NominalValue)} {match.DeltaUnit} | STEP measured: {Format(match.MeasuredValue)} {match.DeltaUnit} | Absolute delta: {Format(match.AbsoluteDelta)} {match.DeltaUnit} | Signed delta (DXF − STEP): {FormatSigned(match.SignedDelta)} {match.DeltaUnit}");
        lines.Add(string.Empty);
        lines.Add("## Unmatched Dimensions – Requires Manual Review");
        lines.Add(string.Empty);
        foreach (var match in UnmatchedDimensions)
            lines.Add($"- Reference: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | Nominal value: {match.NominalValue?.ToString("G17") ?? "-"} | Reason: {match.ReasonCode ?? "-"} ({match.ReasonExplanation ?? "-"})");
        return string.Join(Environment.NewLine, lines);
    }
    private static string FormatStatus(Enums.DimensionMatchStatus status) => status switch
    {
        Enums.DimensionMatchStatus.Matched => "Matched",
        Enums.DimensionMatchStatus.Mismatched => "Mismatched",
        Enums.DimensionMatchStatus.Unmatched => "Unmatched",
        Enums.DimensionMatchStatus.MissingDimension => "Missing Dimension",
        _ => "Unknown"
    };
    private static string Format(double? value) => value is double number && double.IsFinite(number) ? number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-";
    private static string FormatSigned(double? value) => value is double number && double.IsFinite(number) ? $"{number:+0.###;-0.###;0}" : "-";
}
