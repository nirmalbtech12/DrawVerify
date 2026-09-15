namespace DrawVerify.Core.Models;

public sealed record VersionDifference(string Category, string Identifier, string Change, string? PreviousValue = null, string? CurrentValue = null);

public sealed record VersionComparison
{
    public Guid PackageId { get; init; }
    public int PriorRevision { get; init; }
    public int CurrentRevision { get; init; }
    public IReadOnlyList<VersionDifference> FileChanges { get; init; } = Array.Empty<VersionDifference>();
    public IReadOnlyList<VersionDifference> DimensionChanges { get; init; } = Array.Empty<VersionDifference>();
    public IReadOnlyList<VersionDifference> GdtChanges { get; init; } = Array.Empty<VersionDifference>();
    public IReadOnlyList<VersionDifference> MetadataChanges { get; init; } = Array.Empty<VersionDifference>();
    public bool EvidenceAvailable { get; init; } = true;
}
