namespace DrawVerify.Core.Models;

public sealed record ViewerAccessChange(IReadOnlyList<ViewerAccessTarget> Targets, DateTime? ExpiresUtc = null, string? Note = null);
public sealed record ViewerAccessMutationResult(int AffectedTargetCount, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
    public string Summary => $"Viewer access updated for {AffectedTargetCount} target{(AffectedTargetCount == 1 ? "" : "s")}.";
}
