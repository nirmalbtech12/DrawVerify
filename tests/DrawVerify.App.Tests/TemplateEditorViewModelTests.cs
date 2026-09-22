using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class TemplateEditorViewModelTests
{
    [Fact(DisplayName = "TSD-01 through TSD-04 ViewModel opens and inserts requested placeholders")]
    public async Task Authorized_view_model_supports_editor_tools_and_geometry()
    {
        var session = new DrawVerify.Application.Services.SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("admin", [SystemRoles.Admin]));
        var service = new FakeEditorService();
        var vm = new TemplateEditorViewModel(service, session, new RoleAuthorization());
        await vm.OpenAsync(Guid.NewGuid());
        await vm.InsertAsync(TemplatePlaceholderType.Signature, "Maker Signature", "Maker", true);
        await vm.InsertAsync(TemplatePlaceholderType.Date, "Date");
        var first = vm.Placeholders[0];
        vm.SetPosition(first, new TemplatePlaceholderPosition { X = .2, Y = .2, Width = .3, Height = .1 });
        Assert.True(vm.CanEdit);
        Assert.Equal(.2, vm.Placeholders[0].Position.X);
        Assert.Equal(2, vm.Placeholders.Count);
    }

    [Fact(DisplayName = "TSD-08 unauthorized ViewModel cannot mutate editor")]
    public async Task Unauthorized_view_model_is_read_only()
    {
        var session = new DrawVerify.Application.Services.SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("viewer", [SystemRoles.Viewer]));
        var service = new FakeEditorService(); var vm = new TemplateEditorViewModel(service, session, new RoleAuthorization());
        Assert.False(vm.CanEdit);
        Assert.False(vm.InsertDateCommand.CanExecute(null));
        await vm.OpenAsync(Guid.NewGuid());
        Assert.Empty(vm.Placeholders);
    }

    [Fact(DisplayName = "TSD-04 invalid geometry retains the last valid position")]
    public async Task Invalid_geometry_does_not_replace_last_valid_position()
    {
        var session = new DrawVerify.Application.Services.SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("admin", [SystemRoles.Admin]));
        var vm = new TemplateEditorViewModel(new FakeEditorService(), session, new RoleAuthorization());
        await vm.OpenAsync(Guid.NewGuid());
        await vm.InsertAsync(TemplatePlaceholderType.ProjectName, "Project");
        var placeholder = vm.Placeholders[0]; vm.SetPosition(placeholder, new TemplatePlaceholderPosition { X = .2, Y = .2, Width = .3, Height = .1 });
        Assert.Throws<InvalidOperationException>(() => vm.SetPosition(vm.Placeholders[0], new TemplatePlaceholderPosition { X = .9, Y = .2, Width = .3, Height = .1 }));
        Assert.Equal(.2, vm.Placeholders[0].Position.X);
        Assert.Equal(.3, vm.Placeholders[0].Position.Width);
    }

    private sealed class FakeEditorService : IReportTemplateEditorService
    {
        public Task<TemplateEditorDocument> OpenAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default) => Task.FromResult(new TemplateEditorDocument { DocxBytes = [1] });
        public Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default) => Task.FromResult(document with { Placeholders = document.Placeholders.Append(placeholder).ToArray() });
        public Task<ReportTemplate> SaveAsync(ReportTemplate template, TemplateEditorDocument document, int expectedVersion, string actorId, CancellationToken cancellationToken = default) => Task.FromResult(template with { Version = expectedVersion + 1, Placeholders = document.Placeholders });
        public Task<TemplatePreviewData> PreviewAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default) => Task.FromResult(new TemplatePreviewData { DocxBytes = document.DocxBytes });
    }
}
