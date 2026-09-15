namespace DrawVerify.Core.Models;
public sealed record WorkflowTriggerDefinition
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> DocumentTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Projects { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Departments { get; init; } = Array.Empty<string>();
    public int? MinimumRevision { get; init; }
    public int? RiskLevel { get; init; }
    public decimal? MinimumValue { get; init; }
}
