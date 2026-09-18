using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ReportTemplateConfigurationViewModelTests
{
    [Fact(DisplayName = "TSD-AC12 ViewModel permits editing only for Admin configuration permission")]
    public void View_model_command_state_is_read_only_for_non_admin()
    {
        var service = new ServiceFake();
        var viewer = new SessionAuthenticationService();
        viewer.SignIn(new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer }));
        var model = new ReportTemplateConfigurationViewModel(service, viewer, new RoleAuthorization());
        Assert.False(model.CanEdit);
        Assert.False(model.NewCommand.CanExecute(null));
        Assert.False(model.SaveCommand.CanExecute(null));
    }

    [Fact(DisplayName = "TSD-AC01 TSD-AC12 Admin ViewModel exposes edit commands")]
    public void Admin_view_model_is_editable_and_loads_template()
    {
        var service = new ServiceFake { Items = new[] { Template("Configured") } };
        var admin = new SessionAuthenticationService();
        admin.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
        var model = new ReportTemplateConfigurationViewModel(service, admin, new RoleAuthorization());

        Assert.True(model.CanEdit);
        Assert.True(model.NewCommand.CanExecute(null));
        Assert.True(SpinWait.SpinUntil(() => model.SelectedTemplate is not null, TimeSpan.FromSeconds(2)));
        Assert.True(model.SaveCommand.CanExecute(null));
    }

    [Fact(DisplayName = "TSD-AC01 authorized ViewModel loads templates")]
    public void Authorized_view_model_loads_repository_templates()
    {
        var service = new ServiceFake { Items = new[] { Template("Configured") } };
        var admin = new SessionAuthenticationService();
        admin.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
        var model = new ReportTemplateConfigurationViewModel(service, admin, new RoleAuthorization());
        Assert.True(SpinWait.SpinUntil(() => model.Templates.Count == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal("Configured", model.Templates[0].Name);
    }

    private static ReportTemplate Template(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private sealed class ServiceFake : IReportTemplateService
    {
        public IReadOnlyList<ReportTemplate> Items { get; init; } = Array.Empty<ReportTemplate>();
        public Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items);
        public Task<ReportTemplate> CreateAsync(string name, string actorId, CancellationToken cancellationToken = default) => Task.FromResult(Template(name));
        public Task<ReportTemplate> DuplicateAsync(Guid id, string name, string actorId, CancellationToken cancellationToken = default) => Task.FromResult(Template(name));
        public Task<ReportTemplate> SaveAsync(ReportTemplate template, int expectedVersion, string actorId, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task SetDefaultAsync(Guid id, string actorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, int expectedVersion, string actorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ReportTemplateVersion>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplateVersion>>(Array.Empty<ReportTemplateVersion>());
        public Task<ReportTemplate> RestoreAsync(Guid id, int version, string actorId, CancellationToken cancellationToken = default) => Task.FromResult(Template("restored"));
        public Task AssignAsync(ReportTemplateAssignment assignment, string actorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
