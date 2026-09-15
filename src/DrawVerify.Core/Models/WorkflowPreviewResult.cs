namespace DrawVerify.Core.Models;
public sealed record WorkflowPreviewResult(IReadOnlyList<string> Stages, IReadOnlyList<string> Recipients, IReadOnlyList<string> Errors, bool MatchesTrigger);
