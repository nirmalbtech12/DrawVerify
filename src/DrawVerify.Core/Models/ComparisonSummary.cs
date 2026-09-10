using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed record DimensionTypeSummary(
    DimensionType DimensionType,
    int MatchedDimensions,
    int MismatchedDimensions,
    int UnmatchedDimensions,
    int ExtraDimensions = 0)
{
    public int TotalDimensions => MatchedDimensions + MismatchedDimensions + UnmatchedDimensions + ExtraDimensions;
}

public sealed record ComparisonSummary
{
    public int TotalDimensions { get; init; }
    public int MatchedDimensions { get; init; }
    public int MismatchedDimensions { get; init; }
    public int UnmatchedDimensions { get; init; }
    public int MissingDimensions { get; init; }
    public int ExtraDimensions { get; init; }
    public double? MatchPercentage { get; init; }
    public IReadOnlyList<DimensionTypeSummary> DimensionTypes { get; init; } = Array.Empty<DimensionTypeSummary>();

    public static ComparisonSummary From(IEnumerable<DimensionMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var rows = matches.ToArray();
        var matched = rows.Count(match => match.Status == DimensionMatchStatus.Matched);
        var mismatched = rows.Count(match => match.Status == DimensionMatchStatus.Mismatched);
        var unmatched = rows.Count(match => match.Status == DimensionMatchStatus.Unmatched);
        var missing = rows.Count(match => match.Status == DimensionMatchStatus.MissingDimension);
        var extra = rows.Count(match => match.Status == DimensionMatchStatus.ExtraDimension || match.Status == DimensionMatchStatus.MissingDimension);
        var total = matched + mismatched + unmatched + extra;

        foreach (var match in rows)
        {
            if (match.Status is not (DimensionMatchStatus.Matched or DimensionMatchStatus.Mismatched or DimensionMatchStatus.Unmatched or DimensionMatchStatus.MissingDimension or DimensionMatchStatus.ExtraDimension))
                throw new InvalidOperationException($"Unsupported dimension match status '{match.Status}'.");
        }

        var typedRows = rows
            .Where(match => match.Status is DimensionMatchStatus.Matched or DimensionMatchStatus.Mismatched or DimensionMatchStatus.Unmatched or DimensionMatchStatus.ExtraDimension or DimensionMatchStatus.MissingDimension)
            .Where(match => match.DimensionType is not null)
            .ToArray();
        var byType = Enum.GetValues<DimensionType>()
            .Select(type => new DimensionTypeSummary(
                type,
                typedRows.Count(match => match.DimensionType == type && match.Status == DimensionMatchStatus.Matched),
                typedRows.Count(match => match.DimensionType == type && match.Status == DimensionMatchStatus.Mismatched),
                typedRows.Count(match => match.DimensionType == type && match.Status == DimensionMatchStatus.Unmatched),
                typedRows.Count(match => match.DimensionType == type && (match.Status == DimensionMatchStatus.ExtraDimension || match.Status == DimensionMatchStatus.MissingDimension))))
            .ToArray();

        return new ComparisonSummary
        {
            TotalDimensions = total,
            MatchedDimensions = matched,
            MismatchedDimensions = mismatched,
            UnmatchedDimensions = unmatched,
            MissingDimensions = missing,
            ExtraDimensions = extra,
            MatchPercentage = total == 0 ? null : matched * 100d / total,
            DimensionTypes = byType
        };
    }
}
