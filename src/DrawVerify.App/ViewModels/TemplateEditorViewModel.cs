using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Data;
using System.Globalization;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class TemplateEditorViewModel : TaskScreenViewModel
{
    private readonly IReportTemplateEditorService _editor;
    private readonly IReportTemplateService? _templates;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;
    private TemplateEditorDocument? _document;
    private ReportTemplate? _template;
    private string _message = string.Empty;
    private string _validationErrors = string.Empty;

    public ObservableCollection<TemplatePlaceholder> Placeholders { get; } = new();
    public ObservableCollection<ReportTemplate> Templates { get; } = new();
    public IReadOnlyList<string> SignatureRoleOptions { get; } = ["Maker", "Checker", "Approver", "Custom role"];
    public string SignatureRole { get; set; } = string.Empty;
    public string CustomRole { get; set; } = string.Empty;
    public string SignatureLabel { get; set; } = string.Empty;
    public bool SignatureRequired { get; set; }
    public string CustomTextName { get; set; } = string.Empty;
    public TemplatePlaceholder? SelectedPlaceholder { get; private set; }
    public ReportTemplate? SelectedTemplate { get => _template; set { _template = value; RaisePropertyChanged(nameof(SelectedTemplate)); } }
    public TemplateEditorDocument? Document { get => _document; private set { _document = value; RaisePropertyChanged(nameof(Document)); } }
    public bool CanEdit => _session.IsAuthenticated && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration);
    public string Message { get => _message; private set { _message = value; RaisePropertyChanged(nameof(Message)); } }
    public string ValidationErrors { get => _validationErrors; private set { _validationErrors = value; RaisePropertyChanged(nameof(ValidationErrors)); } }
    public ICommand InsertSignatureCommand { get; }
    public ICommand InsertDateCommand { get; }
    public ICommand InsertDocumentNumberCommand { get; }
    public ICommand InsertRevisionCommand { get; }
    public ICommand InsertProjectNameCommand { get; }
    public ICommand InsertCustomTextCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand OpenTemplateCommand { get; }
    public ICommand AlignLeftCommand { get; }
    public ICommand AlignCenterCommand { get; }
    public ICommand AlignTopCommand { get; }

    public TemplateEditorViewModel(IReportTemplateEditorService editor, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger? logger = null, IReportTemplateService? templates = null)
    {
        _editor = editor; _session = session; _authorization = authorization; _logger = logger; _templates = templates;
        InsertSignatureCommand = new Command(async _ => await InsertSignatureFromEditorAsync(), _ => CanEdit);
        InsertDateCommand = new Command(async _ => await InsertAsync(TemplatePlaceholderType.Date, "Date"), _ => CanEdit);
        InsertDocumentNumberCommand = new Command(async _ => await InsertAsync(TemplatePlaceholderType.DocumentNumber, "Document number"), _ => CanEdit);
        InsertRevisionCommand = new Command(async _ => await InsertAsync(TemplatePlaceholderType.Revision, "Revision"), _ => CanEdit);
        InsertProjectNameCommand = new Command(async _ => await InsertAsync(TemplatePlaceholderType.ProjectName, "Project name"), _ => CanEdit);
        InsertCustomTextCommand = new Command(async _ => await InsertAsync(TemplatePlaceholderType.CustomText, CustomTextName), _ => CanEdit);
        SaveCommand = new Command(async _ => await SaveAsync(), _ => CanEdit && SelectedTemplate is not null && Document is not null);
        PreviewCommand = new Command(async _ => await PreviewAsync(), _ => CanEdit && Document is not null);
        OpenTemplateCommand = new Command(async p => await OpenTemplateAsync(p as ReportTemplate), _ => CanEdit);
        AlignLeftCommand = new Command(_ => { AlignSelected(position => position with { X = 0 }); return Task.CompletedTask; }, _ => CanEdit && SelectedPlaceholder is not null);
        AlignCenterCommand = new Command(_ => { AlignSelected(position => position with { X = Math.Max(0, (1 - position.Width) / 2) }); return Task.CompletedTask; }, _ => CanEdit && SelectedPlaceholder is not null);
        AlignTopCommand = new Command(_ => { AlignSelected(position => position with { Y = 0 }); return Task.CompletedTask; }, _ => CanEdit && SelectedPlaceholder is not null);
        if (_templates is not null) _ = LoadTemplatesAsync();
    }

    public async Task LoadTemplatesAsync(CancellationToken cancellationToken = default)
    { try { Templates.Clear(); if (_templates is not null) foreach (var template in await _templates.ListAsync(cancellationToken)) Templates.Add(template); } catch (Exception ex) { Handle(ex, "load templates"); } }

    public async Task OpenTemplateAsync(ReportTemplate? template, CancellationToken cancellationToken = default)
    { if (template is null) throw new InvalidOperationException("Select a template to open."); SelectedTemplate = template; await OpenAsync(template.Id, template.Version, cancellationToken); }

    public async Task OpenAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default)
    {
        try { Document = await _editor.OpenAsync(templateId, version, cancellationToken); Placeholders.Clear(); foreach (var item in Document.Placeholders) Placeholders.Add(item); Message = "Template opened for editing."; ValidationErrors = string.Empty; }
        catch (Exception ex) { Handle(ex, "open"); }
    }

    public async Task InsertAsync(TemplatePlaceholderType type, string label, string? role = null, bool required = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Document is null) throw new InvalidOperationException("Open a template before inserting a placeholder.");
            if (string.IsNullOrWhiteSpace(label)) throw new InvalidOperationException("Placeholder label is required.");
            if (type == TemplatePlaceholderType.Signature && string.IsNullOrWhiteSpace(role)) throw new InvalidOperationException("Select Maker, Checker, Approver, or enter a custom signature role.");
            var placeholder = new TemplatePlaceholder { Type = type, Label = label.Trim(), Role = type == TemplatePlaceholderType.Signature ? role?.Trim() : null, IsRequired = required, Position = new TemplatePlaceholderPosition { X = 0.05, Y = Math.Min(0.9, 0.05 + Placeholders.Count * 0.1), Width = 0.25, Height = 0.08 } };
            Document = await _editor.InsertPlaceholderAsync(Document, placeholder, cancellationToken); Placeholders.Add(placeholder); IsDirty = true; Message = "Placeholder inserted.";
        }
        catch (Exception ex) { Handle(ex, "insert placeholder"); }
    }

    public void SetPosition(TemplatePlaceholder placeholder, TemplatePlaceholderPosition position)
    {
        if (!CanEdit) throw new UnauthorizedAccessException("Template editing is not authorized.");
        position.Validate();
        var replacement = placeholder with { Position = position }; Replace(replacement); IsDirty = true;
    }

    public void SelectPlaceholder(TemplatePlaceholder placeholder) => SelectedPlaceholder = placeholder;

    public void AlignSelected(Func<TemplatePlaceholderPosition, TemplatePlaceholderPosition> alignment)
    {
        if (SelectedPlaceholder is null) return;
        SetPosition(SelectedPlaceholder, alignment(SelectedPlaceholder.Position));
    }

    private Task InsertSignatureFromEditorAsync()
    {
        var role = string.Equals(SignatureRole, "Custom role", StringComparison.OrdinalIgnoreCase) ? CustomRole : SignatureRole;
        return InsertAsync(TemplatePlaceholderType.Signature, SignatureLabel, role, SignatureRequired);
    }

    private async Task SaveAsync()
    {
        try { if (SelectedTemplate is null || Document is null) throw new InvalidOperationException("A template must be open before saving."); SelectedTemplate = await _editor.SaveAsync(SelectedTemplate, Document, SelectedTemplate.Version, _session.CurrentUser!.UserId); IsDirty = false; Message = "Template saved as a new version."; }
        catch (Exception ex) { Handle(ex, "save template"); }
    }

    private async Task PreviewAsync()
    { try { if (Document is null) throw new InvalidOperationException("Open a template before previewing."); var preview = await _editor.PreviewAsync(Document); Message = $"Preview generated with {preview.Samples.Count} sample values."; } catch (Exception ex) { Handle(ex, "preview template"); } }

    private void Replace(TemplatePlaceholder replacement)
    { var index = Placeholders.IndexOf(Placeholders.First(x => x.Id == replacement.Id)); Placeholders[index] = replacement; SelectedPlaceholder = replacement; if (Document is not null) Document = Document with { Placeholders = Placeholders.ToArray() }; }
    private void Handle(Exception ex, string operation) { if (ex is not UnauthorizedAccessException) _logger?.Log(ex, $"Template editor {operation}", "Template editor operation failed."); Message = ex is UnauthorizedAccessException ? "Template editor access denied." : $"Unable to {operation}."; ValidationErrors = ex.Message; }
    public void ShowValidationError(string message) { ValidationErrors = message; Message = "Placeholder update rejected."; }
    private sealed class Command(Func<object?, Task> execute, Predicate<object?> can) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => can(parameter); public async void Execute(object? parameter) => await execute(parameter); }
}

public sealed class NormalizedCoordinateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double normalized && double.TryParse(parameter?.ToString(), out var scale) ? normalized * scale : 0d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
