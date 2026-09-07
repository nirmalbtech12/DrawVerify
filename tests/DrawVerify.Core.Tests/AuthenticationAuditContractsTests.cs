using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class AuthenticationAuditContractsTests
{
    [Fact(DisplayName = "TD-04 validates audit queries, retention, canonical actions, and Admin-only permission")]
    public void Contracts_reject_invalid_values_and_redact_secrets_by_shape()
    {
        Assert.Throws<ArgumentException>(() => new AuditLogQuery(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuditLogQuery(Page: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuditLogQuery(PageSize: 1001).Validate());
        Assert.Throws<InvalidOperationException>(() => new AuditLogConfiguration { RetentionDays = -1 }.Validate());

        var authorization = new RoleAuthorization();
        Assert.True(authorization.IsAllowed(new AuthenticatedUser("ADMIN", new[] { "Admin" }, 0), RolePermission.ViewAuditLog));
        Assert.False(authorization.IsAllowed(new AuthenticatedUser("USER", new[] { "Maker" }, 0), RolePermission.ViewAuditLog));

        var entry = new AuthenticationAuditEntry("ADMIN", "USER", DateTimeOffset.UtcNow, "UserModified", "Success", ChangedFields: "[\"PasswordReset\"]");
        Assert.Null(entry.OldValues);
        Assert.Null(entry.NewValues);
        Assert.DoesNotContain("password", string.Join('|', new[] { entry.OldValues, entry.NewValues, entry.RolesBefore, entry.RolesAfter }), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(entry.Action, new[] { "UserCreated", "UserModified", "UserDeactivated", "RoleAssigned", "RoleRemoved", "RolesUpdated", "LoginSuccess", "LoginFailure", "AccountLocked", "Logout" });
    }
}
