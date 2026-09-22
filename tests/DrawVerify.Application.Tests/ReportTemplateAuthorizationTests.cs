using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateAuthorizationTests
{
    [Fact(DisplayName = "TSD-08 editor service denies unauthenticated and unauthorized access")]
    public async Task Editor_service_enforces_access_boundary()
    {
        foreach (var user in new AuthenticatedUser?[] { null, new("viewer", [SystemRoles.Viewer]) })
        {
            var session = new SessionAuthenticationService();
            if (user is not null) session.SignIn(user);
            var editor = new ReportTemplateEditorService(null!, null!, null!, null!, session, new RoleAuthorization());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => editor.OpenAsync(Guid.NewGuid()));
        }
    }

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

    [Fact(DisplayName = "TSD-AC12 Admin can read and create templates")]
    public async Task Admin_can_manage_templates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-auth-admin-{Guid.NewGuid():N}.db");
        try
        {
            var auth = new SessionAuthenticationService();
            auth.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
            var service = new ReportTemplateService(new SqliteReportTemplateRepository(path), auth, new RoleAuthorization());

            var created = await service.CreateAsync("Admin template", "ignored");

            var templates = await service.ListAsync();
            Assert.Single(templates);
            Assert.Equal(created.Id, templates[0].Id);
            Assert.Equal("Admin template", created.Name);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
