namespace DrawVerify.Core.Models;
public sealed record WorkflowRevisionPolicy
{
    public bool CreateRevisionOnSubmission { get; init; } = true;
    public string NumberFormat { get; init; } = "Sequential";
    public bool RetainRejectedRevisions { get; init; } = true;
}
