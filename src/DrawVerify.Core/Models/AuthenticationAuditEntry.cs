namespace DrawVerify.Core.Models;

public sealed record AuthenticationAuditEntry(
    string? ActorUsername, string Username, DateTimeOffset TimestampUtc, string Action, string Result,
    string? FailureReason = null, string? RolesBefore = null, string? RolesAfter = null,
    string? SourceIdentifier = null, string? ChangedFields = null, string? OldValues = null,
    string? NewValues = null, string? DeactivationReason = null, int? FailedAttemptCount = null);
