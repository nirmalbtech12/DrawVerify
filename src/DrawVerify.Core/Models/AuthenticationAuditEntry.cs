namespace DrawVerify.Core.Models;

public sealed record AuthenticationAuditEntry(string? ActorUsername, string Username, DateTimeOffset TimestampUtc, string Action, string Result, string? FailureReason = null);
