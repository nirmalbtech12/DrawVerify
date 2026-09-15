namespace DrawVerify.Core.Models;
public sealed record WorkflowConfigurationAudit(string ActorId, DateTime TimestampUtc, int? PreviousVersion, int NewVersion, string PreviousValue, string NewValue);
