namespace DrawVerify.Core.Models;

public sealed class VerificationReport
{
    public IReadOnlyList<DimensionMatch> DimensionMatches { get; init; } = Array.Empty<DimensionMatch>();
    public ComparisonSummary ComparisonSummary => DrawVerify.Core.Models.ComparisonSummary.From(DimensionMatches);
    public IReadOnlyList<DimensionMatch> UnmatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Unmatched).ToArray();
    public IReadOnlyList<DimensionMatch> MismatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Mismatched).ToArray();
    public IReadOnlyList<DimensionMatch> ExtraDimensions => DimensionMatches.Where(match => match.Status is Enums.DimensionMatchStatus.ExtraDimension or Enums.DimensionMatchStatus.MissingDimension).ToArray();
    public string ToMarkdown()
    {
        var summary = ComparisonSummary;
        var lines = new List<string> { "# Verification Report", string.Empty, "## Comparison Summary", string.Empty,
            $"- Total Dimensions: {summary.TotalDimensions}",
            $"- Matched: {summary.MatchedDimensions}",
            $"- Mismatched: {summary.MismatchedDimensions}",
            $"- Unmatched: {summary.UnmatchedDimensions}",
            $"- Missing Dimensions: {summary.MissingDimensions}",
            $"- Extra Dimensions (in 3D but not in 2D): {summary.ExtraDimensions}",
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
        lines.Add("## Extra Dimensions");
        lines.Add(string.Empty);
        if (ExtraDimensions.Count == 0) lines.Add("- None identified.");
        foreach (var match in ExtraDimensions)
            lines.Add($"- STEP reference: `{match.StepGeometryReference ?? "-"}` | Geometry: {match.GeometryType ?? "-"} | Value: {Format(match.MeasuredValue ?? match.NominalValue)} {match.ToleranceUnit ?? match.DeltaUnit} | Tolerance: {FormatTolerance(match)} | View: {match.ViewName ?? "-"} | Location: {FormatLocation(match)} | Comment: {match.Review?.Comment ?? "-"} | Design update: {match.Review?.RequiresDesignUpdate == true}");
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
        lines.Add("Missing from 3D:");
        if (UnmatchedDimensions.Count == 0)
            lines.Add("- None identified.");
        foreach (var match in UnmatchedDimensions)
            lines.Add($"- Reference: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | Value: {Format(match.NominalValue)} {match.DeltaUnit} | Nominal value: {Format(match.NominalValue)} {match.DeltaUnit} | Tolerance: {FormatTolerance(match)} | Layer: {match.DxfLayer ?? "-"} | Text: {match.DimensionText ?? "-"} | Reason: {match.ReasonCode ?? "-"} ({match.ReasonExplanation ?? "-"}) | Comment: {match.Review?.Comment ?? "-"} | Resolution: {match.Review?.Status.ToString() ?? "Open"}");
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
    private static string FormatTolerance(DimensionMatch match) => match.EffectiveLowerTolerance is double lower && match.EffectiveUpperTolerance is double upper ? $"{Format(lower)} / +{Format(upper)} {match.ToleranceUnit ?? ""}" : "-";
    private static string FormatLocation(DimensionMatch match) => match.LocationX is double x && match.LocationY is double y ? $"({Format(x)}, {Format(y)})" : "Unavailable";
}
