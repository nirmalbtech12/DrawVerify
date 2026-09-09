namespace DrawVerify.Core.Models;

public sealed class VerificationReport
{
    public IReadOnlyList<DimensionMatch> DimensionMatches { get; init; } = Array.Empty<DimensionMatch>();
    public ComparisonSummary ComparisonSummary => DrawVerify.Core.Models.ComparisonSummary.From(DimensionMatches);
    public IReadOnlyList<DimensionMatch> UnmatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Unmatched).ToArray();
    public IReadOnlyList<DimensionMatch> MismatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Mismatched).ToArray();
    public string ToMarkdown()
    {
        var summary = ComparisonSummary;
        var lines = new List<string> { "# Verification Report", string.Empty, "## Comparison Summary", string.Empty,
            $"- Total Dimensions: {summary.TotalDimensions}",
            $"- Matched: {summary.MatchedDimensions}",
            $"- Mismatched: {summary.MismatchedDimensions}",
            $"- Unmatched: {summary.UnmatchedDimensions}",
            $"- Missing Dimensions: {summary.MissingDimensions}",
            $"- Match Percentage: {(summary.MatchPercentage is double percentage ? percentage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%" : "N/A")}",
            string.Empty,
            "### Summary by Dimension Type",
            string.Empty };
        foreach (var type in summary.DimensionTypes)
            lines.Add($"- {type.DimensionType}: {type.MatchedDimensions} Matched, {type.MismatchedDimensions} Mismatched, {type.UnmatchedDimensions} Unmatched");
        lines.Add(string.Empty);
        lines.Add("## Dimension Status Summary");
        lines.Add(string.Empty);
        foreach (var type in summary.DimensionTypes)
            lines.Add($"- {type.DimensionType}: {type.MatchedDimensions} Matched, {type.MismatchedDimensions} Mismatched, {type.UnmatchedDimensions} Unmatched");
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
