using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateAssignmentTests
{
    [Fact(DisplayName = "TSD-AC11 project assignment resolves before organization default")]
    public async Task Project_assignment_has_precedence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-assignment-{Guid.NewGuid():N}.db");
        try
        {
            var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
            var repository = new SqliteReportTemplateRepository(path);
            var service = new ReportTemplateService(repository, auth, new RoleAuthorization());
            var organization = await service.CreateAsync("Organization", "admin");
            var project = await service.CreateAsync("Project", "admin");
            await service.SetDefaultAsync(organization.Id, "admin");
            await service.AssignAsync(new ReportTemplateAssignment { TemplateId = project.Id, ProjectId = "project-1" }, "admin");
            var resolved = await repository.ResolveAsync("project-1", null);
            Assert.Equal(project.Id, resolved!.Id);
            var assignmentAudit = Assert.Single((await repository.GetAuditAsync(project.Id)).Where(entry => entry.Action == "Assigned"));
            Assert.Equal(project.Id, assignmentAudit.TemplateId);
            Assert.Equal("admin", assignmentAudit.ActorId);
            Assert.NotEqual(default, assignmentAudit.TimestampUtc);
            Assert.Equal("Assigned", assignmentAudit.Action);
            Assert.Equal("Project=project-1; Workflow=", assignmentAudit.Summary);
            Assert.True(assignmentAudit.Version > 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignAsync(new ReportTemplateAssignment { TemplateId = Guid.NewGuid() }, "admin"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
