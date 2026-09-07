namespace DrawVerify.Core.Models;

public sealed record RoleAssignmentAuditEntry(string ActorUsername, string AffectedUsername, IReadOnlyList<string> RolesBefore, IReadOnlyList<string> RolesAfter, DateTimeOffset TimestampUtc, string ActionType);

public enum RoleReplacementFailure
{
    None,
    UserNotFound,
    StaleRoleVersion,
    LastAdministrator
}

public sealed record RoleReplacementResult(bool Succeeded, RoleReplacementFailure Failure = RoleReplacementFailure.None, string? Message = null, long RoleVersion = 0);
