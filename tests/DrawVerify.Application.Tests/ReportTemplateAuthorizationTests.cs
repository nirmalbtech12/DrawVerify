using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateAuthorizationTests
{
    [Theory(DisplayName = "TSD-AC12 only authenticated Admins can mutate templates")]
    [InlineData(null)] [InlineData(SystemRoles.Viewer)] [InlineData(SystemRoles.Maker)] [InlineData(SystemRoles.Checker)] [InlineData(SystemRoles.Approver)]
    public async Task Unauthorized_roles_are_denied(string? role)
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-auth-{Guid.NewGuid():N}.db");
        try
        {
            var auth = new SessionAuthenticationService();
            if (role is not null) auth.SignIn(new AuthenticatedUser("user", new[] { role }));
            var service = new ReportTemplateService(new SqliteReportTemplateRepository(path), auth, new RoleAuthorization());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync("x", "user"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
