using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class AuditLogViewModelTests
{
    [Fact(DisplayName = "TD-06 maps filters, rows, and CSV export for an Admin")]
    public async Task Admin_viewer_maps_filters_and_results()
    {
        var repository = new AuditRepositoryFake { Page = new AuditLogPage(new[] { new AuthenticationAuditEntry("ADMIN", "USER", DateTimeOffset.UtcNow, "LoginSuccess", "Success") }, 1, 100, 1), Csv = "Actor,Username\r\nADMIN,USER\r\n" };
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }, 0));
        var vm = new AuditLogViewModel(new AuditLogService(repository, session)) { Action = "LoginSuccess", Username = "USER", Actor = "ADMIN" };
        await vm.LoadAsync(); await vm.ExportAsync();
        Assert.Single(vm.Entries); Assert.Equal("Audit log exported.", vm.Message); Assert.Contains("ADMIN", vm.Csv);
        Assert.Equal("LoginSuccess", repository.LastQuery!.Action); Assert.Equal("USER", repository.LastQuery.Username); Assert.Equal("ADMIN", repository.LastQuery.ActorUsername);
    }

    [Fact(DisplayName = "TD-06 surfaces denied access and export failures")]
    public async Task Viewer_surfaces_service_failure_without_false_success()
    {
        var repository = new AuditRepositoryFake { Throw = true };
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("USER", new[] { "Viewer" }, 0));
        var vm = new AuditLogViewModel(new AuditLogService(repository, session));
        await vm.LoadAsync(); await vm.ExportAsync();
        Assert.Contains("administrator", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.Csv);
    }

    private sealed class AuditRepositoryFake : IAuthenticationAuditRepository
    {
        public AuditLogPage? Page { get; init; }
        public string? Csv { get; init; }
        public bool Throw { get; init; }
        public AuditLogQuery? LastQuery { get; private set; }
        public Task WriteAsync(AuthenticationAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query; if (Throw) return Task.FromException<AuditLogPage>(new InvalidOperationException("retrieval failed"));
            return Task.FromResult(Page ?? new AuditLogPage(Array.Empty<AuthenticationAuditEntry>(), 1, 100, 0));
        }
    }
}
