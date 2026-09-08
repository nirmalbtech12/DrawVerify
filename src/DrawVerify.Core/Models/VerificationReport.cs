namespace DrawVerify.Core.Models;

public sealed class VerificationReport
{
    public IReadOnlyList<DimensionMatch> DimensionMatches { get; init; } = Array.Empty<DimensionMatch>();
    public IReadOnlyList<DimensionMatch> UnmatchedDimensions => DimensionMatches.Where(match => match.Status == Enums.DimensionMatchStatus.Unmatched).ToArray();
    public string ToMarkdown()
    {
        var lines = new List<string> { "# Verification Report", string.Empty, "## Unmatched Dimensions – Requires Manual Review", string.Empty };
        foreach (var match in UnmatchedDimensions)
            lines.Add($"- Reference: `{match.DxfDimensionReference}` | Type: {match.DimensionType?.ToString() ?? "-"} | Nominal value: {match.NominalValue?.ToString("G17") ?? "-"} | Reason: {match.ReasonCode ?? "-"} ({match.ReasonExplanation ?? "-"})");
        return string.Join(Environment.NewLine, lines);
    }
}
