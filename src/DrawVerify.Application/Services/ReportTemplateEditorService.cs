using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class ReportTemplateEditorService : IReportTemplateEditorService
{
    private readonly IReportTemplateRepository _templates;
    private readonly IReportTemplateDocumentRepository _documents;
    private readonly IWordTemplateEditor _editor;
    private readonly IWordTemplatePreviewService _preview;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;

    public ReportTemplateEditorService(IReportTemplateRepository templates, IReportTemplateDocumentRepository documents, IWordTemplateEditor editor, IWordTemplatePreviewService preview, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger? logger = null)
    { _templates = templates; _documents = documents; _editor = editor; _preview = preview; _session = session; _authorization = authorization; _logger = logger; }

    public async Task<TemplateEditorDocument> OpenAsync(Guid templateId, int? version = null, CancellationToken cancellationToken = default)
    {
        EnsureAccess();
        var document = await _documents.GetAsync(templateId, version, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The template Word document was not found.");
        try { return await _editor.OpenAsync(document.Content, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { _logger?.Log(ex, "Open Word template editor", $"Unable to open templateId={templateId}, version={document.Version}."); throw; }
    }

    public async Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default)
    { EnsureAccess(); return await _editor.InsertPlaceholderAsync(document, placeholder, cancellationToken).ConfigureAwait(false); }

    public async Task<ReportTemplate> SaveAsync(ReportTemplate template, TemplateEditorDocument document, int expectedVersion, string actorId, CancellationToken cancellationToken = default)
    {
        EnsureAccess();
        if (_templates is not IReportTemplatePublisher publisher)
            throw new InvalidOperationException("Atomic template publication is unavailable; the template was not saved.");
        ReportTemplatePlaceholderValidator.Validate(document.Placeholders);
        var bytes = await _editor.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        var updated = template with { Placeholders = document.Placeholders };
        ReportTemplateValidator.Validate(updated);
        var audit = new ReportTemplateAuditEntry { TemplateId = template.Id, Version = expectedVersion + 1, ActorId = actorId, Action = "Modified", Summary = template.Name };
        var source = await _documents.GetAsync(template.Id, expectedVersion, cancellationToken).ConfigureAwait(false);
        var stored = new ReportTemplateDocument { TemplateId = template.Id, Version = expectedVersion + 1, SourceId = source?.SourceId ?? Guid.NewGuid(), SourceFileName = source?.SourceFileName ?? "template.docx", Content = bytes, TemplateMapping = TemplateMappingSnapshot.FromTemplate(template.Id, expectedVersion + 1, document.Placeholders) };
        try
        {
            return await publisher.PublishAsync(updated, expectedVersion, audit, stored, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger?.Log(ex, "Save Word template editor", $"Unable to save templateId={template.Id}, expectedVersion={expectedVersion}."); throw; }
    }

    public async Task<TemplatePreviewData> PreviewAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default)
    { EnsureAccess(); try { return await _preview.PreviewAsync(document, cancellationToken).ConfigureAwait(false); } catch (Exception ex) { _logger?.Log(ex, "Preview Word template editor", "Unable to render template preview."); throw; } }

    private void EnsureAccess()
    { if (!_session.IsAuthenticated || !_authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration)) throw new UnauthorizedAccessException("Only an authenticated Admin can edit report templates."); }
}
