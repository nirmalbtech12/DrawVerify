namespace DrawVerify.Core.Models;

public sealed record RoleAssignmentAuditEntry(string ActorUsername, string AffectedUsername, IReadOnlyList<string> RolesBefore, IReadOnlyList<string> RolesAfter, DateTimeOffset TimestampUtc, string ActionType);
