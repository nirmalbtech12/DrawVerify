using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ReportTemplateConfigurationViewModelTests
{
    [Fact(DisplayName = "TSD-01 authorized ViewModel upload starts conversion without premature publication")]
    public async Task Authorized_upload_uses_conversion_service_and_publishes_result()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-{Guid.NewGuid():N}.pdf");
        try { await File.WriteAllBytesAsync(path, [1, 2]); var conversion = new ConversionFake { Result = Template("Converted") }; var model = new ReportTemplateConfigurationViewModel(new ServiceFake(), Admin(), new RoleAuthorization(), conversion: conversion); await model.UploadPdfAsync(path); Assert.Equal(Path.GetFileName(path), conversion.Source!.FileName); Assert.Contains(model.Templates, x => x.Name == "Converted"); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-02 invalid ViewModel upload is visibly rejected")]
    public async Task Invalid_upload_is_rejected_without_conversion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-{Guid.NewGuid():N}.txt");
        try { await File.WriteAllBytesAsync(path, [1]); var conversion = new ConversionFake(); var model = new ReportTemplateConfigurationViewModel(new ServiceFake(), Admin(), new RoleAuthorization(), conversion: conversion); await model.UploadPdfAsync(path); Assert.Equal(0, conversion.Calls); Assert.NotEmpty(model.ValidationErrors); Assert.Empty(model.Templates); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-06 source-linked template remains available to editor")]
    public async Task Source_linked_template_is_loaded_for_editing()
    {
        var source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] }; var template = Template("Converted") with { Source = source }; var model = new ReportTemplateConfigurationViewModel(new ServiceFake { Items = new[] { template } }, Admin(), new RoleAuthorization());
        Assert.True(SpinWait.SpinUntil(() => model.SelectedTemplate?.Source?.SourceId == source.SourceId, TimeSpan.FromSeconds(2))); Assert.Equal(source.FileName, model.SelectedTemplate!.Source!.FileName);
    }

    [Fact(DisplayName = "TSD-07 reconvert command refreshes the selected editor template")]
    public async Task Reconvert_refreshes_selected_template()
    {
        var original = Template("Old") with { Source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] } }; var replacement = original with { Name = "New", Version = 2 }; var conversion = new ConversionFake { Result = replacement }; var model = new ReportTemplateConfigurationViewModel(new ServiceFake { Items = new[] { original } }, Admin(), new RoleAuthorization(), conversion: conversion);
        Assert.True(SpinWait.SpinUntil(() => model.SelectedTemplate is not null, TimeSpan.FromSeconds(2))); await model.ReconvertAsync(); Assert.Equal("New", model.SelectedTemplate!.Name); Assert.Equal(2, model.SelectedTemplate.Version);
    }

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
    private static SessionAuthenticationService Admin() { var s = new SessionAuthenticationService(); s.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin })); return s; }

    private sealed class ConversionFake : IReportTemplateConversionService
    {
        public int Calls { get; private set; } public ReportTemplateSource? Source { get; private set; } public ReportTemplate Result { get; init; } = Template("Converted");
        public Task<ReportTemplate> ConvertAsync(ReportTemplateSource source, CancellationToken cancellationToken = default) { Calls++; Source = source; return Task.FromResult(Result); }
        public Task<ReportTemplate> ReconvertAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(Result); }
    }

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
