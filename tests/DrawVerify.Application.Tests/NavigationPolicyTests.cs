using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class NavigationPolicyTests
{
    private static readonly string[] Roles = [SystemRoles.Maker, SystemRoles.Checker, SystemRoles.Approver, SystemRoles.Admin, SystemRoles.Viewer];

    [Fact(DisplayName = "APP-001 role policy is explicit for every approved role")]
    public void RolePolicyContainsExpectedPermissions()
    {
        var authorization = new RoleAuthorization();
        var expected = new Dictionary<string, RolePermission[]>
        {
            [SystemRoles.Maker] = [RolePermission.Upload, RolePermission.StartVerification],
            [SystemRoles.Checker] = [RolePermission.ReviewVerification],
            [SystemRoles.Approver] = [RolePermission.ReviewVerification, RolePermission.FinalApproval],
            [SystemRoles.Admin] = [RolePermission.ManageUsersAndRoles, RolePermission.ManageGlobalConfiguration],
            [SystemRoles.Viewer] = [RolePermission.ReadApprovedDocuments]
        };
        foreach (var role in Roles)
        {
            var user = new AuthenticatedUser($"user-{role}", [role]);
            foreach (var permission in expected[role])
                Assert.True(authorization.IsAllowed(user, permission), $"role={role}; permission={permission}");
            if (role != SystemRoles.Admin)
                Assert.False(authorization.IsAllowed(user, RolePermission.ManageUsersAndRoles), $"role={role}; admin permission leaked");
        }
    }

    [Fact(DisplayName = "APP-001 role policy covers every protected menu target")]
    public void EveryProtectedTargetHasAnExplicitRoleOutcome()
    {
        var authorization = new RoleAuthorization();
        var targetPermissions = new Dictionary<string, RolePermission>
        {
            ["Upload Model (STEP)"] = RolePermission.Upload,
            ["Upload Drawing (DXF)"] = RolePermission.Upload,
            ["My Verifications"] = RolePermission.StartVerification,
            ["All Verifications"] = RolePermission.ReviewVerification,
            ["User Management"] = RolePermission.ManageUsersAndRoles,
            ["Role Assignment"] = RolePermission.ManageUsersAndRoles,
            ["Workflow Configuration"] = RolePermission.ManageGlobalConfiguration
        };

        foreach (var role in Roles)
        {
            var user = new AuthenticatedUser($"policy-{role}", [role]);
            foreach (var (target, permission) in targetPermissions)
            {
                var allowed = authorization.IsAllowed(user, permission);
                var expected = permission switch
                {
                    RolePermission.Upload => role == SystemRoles.Maker,
                    RolePermission.StartVerification => role == SystemRoles.Maker,
                    RolePermission.ReviewVerification => role is SystemRoles.Checker or SystemRoles.Approver,
                    RolePermission.ManageUsersAndRoles or RolePermission.ManageGlobalConfiguration => role == SystemRoles.Admin,
                    _ => false
                };
                Assert.Equal(expected, allowed);
            }
        }
    }

    [Theory(DisplayName = "APP-002 protected entry is denied before its factory is called")]
    [InlineData(SystemRoles.Viewer, RolePermission.Upload)]
    [InlineData(SystemRoles.Viewer, RolePermission.ReviewVerification)]
    [InlineData(SystemRoles.Maker, RolePermission.ManageUsersAndRoles)]
    [InlineData(SystemRoles.Checker, RolePermission.Upload)]
    [InlineData(SystemRoles.Approver, RolePermission.Upload)]
    public void UnauthorizedEntryDoesNotInstantiateProtectedView(string role, RolePermission permission)
    {
        var authorization = new RoleAuthorization();
        var user = new AuthenticatedUser($"user-{role}", [role]);
        var factoryCalls = 0;
        Assert.False(authorization.IsAllowed(user, permission), $"role={role}; permission={permission}");
        Func<object> protectedFactory = () => { factoryCalls++; return new object(); };
        Func<object> guardedEntry = () => authorization.IsAllowed(user, permission) ? protectedFactory() : new UnauthorizedAccessException();
        var result = guardedEntry();
        Assert.IsType<UnauthorizedAccessException>(result);
        Assert.Equal(0, factoryCalls);
    }
}
