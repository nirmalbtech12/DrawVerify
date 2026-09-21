using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class ReportTemplateConversionService : IReportTemplateConversionService
{
    private readonly IReportTemplateRepository _templates;
    private readonly IReportTemplateDocumentRepository _documents;
    private readonly IPdfToWordConverter _converter;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;

    public ReportTemplateConversionService(IReportTemplateRepository templates, IReportTemplateDocumentRepository documents, IPdfToWordConverter converter, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger? logger = null)
    {
        _templates = templates; _documents = documents; _converter = converter; _session = session; _authorization = authorization; _logger = logger;
    }

    public async Task<ReportTemplate> ConvertAsync(ReportTemplateSource source, CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        ValidateSource(source);
        try
        {
            await _documents.SaveSourceAsync(source, cancellationToken);
            var converted = await _converter.ConvertAsync(source, cancellationToken);
            ValidateDocx(converted.Content);
            var template = new ReportTemplate { Name = Path.GetFileNameWithoutExtension(source.FileName), Source = source };
            var document = new ReportTemplateDocument { TemplateId = template.Id, Version = 1, SourceId = source.SourceId, SourceFileName = source.FileName, Content = converted.Content };
            var templateAudit = new ReportTemplateAuditEntry { TemplateId = template.Id, ActorId = actor, Action = "Converted", Summary = source.FileName };
            var saved = await PublishAsync(template, document, templateAudit, cancellationToken);
            await RecordAuditAsync(new TemplateConversionAuditEntry { SourceId = source.SourceId, SourceFileName = source.FileName, TemplateId = saved.Id, Version = saved.Version, ActorId = actor, Status = "Success" }, cancellationToken);
            return saved;
        }
        catch (Exception ex)
        {
            _logger?.Log(ex, "PDF to Word conversion", $"sourceId={source.SourceId}; sourceName={source.FileName}; actorId={actor}");
            await RecordAuditSafelyAsync(new TemplateConversionAuditEntry { SourceId = source.SourceId, SourceFileName = source.FileName, ActorId = actor, Status = "Failed", ErrorDetails = BoundError(ex) }, cancellationToken);
            throw new InvalidOperationException($"Unable to convert PDF '{source.FileName}' to Word: {ex.Message}", ex);
        }
    }

    public async Task<ReportTemplate> ReconvertAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var actor = Actor();
        var current = await _templates.GetAsync(templateId, cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Template was not found.");
        if (current.Version != expectedVersion) throw new InvalidOperationException($"Template version conflict: expected {expectedVersion}, current {current.Version}.");
        var source = current.Source ?? throw new InvalidOperationException("The template has no retained PDF source.");
        var document = await _documents.GetAsync(templateId, current.Version, cancellationToken) ?? throw new InvalidOperationException("The template document was not found.");
        try
        {
            var converted = await _converter.ConvertAsync(source, cancellationToken);
            ValidateDocx(converted.Content);
            var replacement = current with { Version = current.Version + 1, EffectiveDate = DateTimeOffset.UtcNow };
            var replacementDocument = new ReportTemplateDocument { TemplateId = templateId, Version = replacement.Version, SourceId = document.SourceId, SourceFileName = document.SourceFileName, Content = converted.Content };
            var templateAudit = new ReportTemplateAuditEntry { TemplateId = templateId, Version = replacement.Version, ActorId = actor, Action = "Reconverted", Summary = source.FileName };
            var saved = await PublishAsync(replacement, replacementDocument, templateAudit, cancellationToken, expectedVersion);
            await RecordAuditAsync(new TemplateConversionAuditEntry { SourceId = source.SourceId, SourceFileName = source.FileName, TemplateId = templateId, Version = saved.Version, ActorId = actor, Status = "Success" }, cancellationToken);
            return saved;
        }
        catch (Exception ex)
        {
            _logger?.Log(ex, "PDF to Word re-conversion", $"sourceId={source.SourceId}; templateId={templateId}; actorId={actor}");
            await RecordAuditSafelyAsync(new TemplateConversionAuditEntry { SourceId = source.SourceId, SourceFileName = source.FileName, TemplateId = templateId, Version = current.Version, ActorId = actor, Status = "Failed", ErrorDetails = BoundError(ex) }, cancellationToken);
            throw new InvalidOperationException($"Unable to re-convert PDF '{source.FileName}' to Word: {ex.Message}", ex);
        }
    }

    public Task<ReportTemplate> ConvertPdfAsync(ReportTemplateSource source, CancellationToken cancellationToken = default) => ConvertAsync(source, cancellationToken);
    public Task<ReportTemplate> ReconvertFromSourceAsync(Guid templateId, int expectedVersion, CancellationToken cancellationToken = default) => ReconvertAsync(templateId, expectedVersion, cancellationToken);

    private string Actor() => _session.IsAuthenticated && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration) ? _session.CurrentUser!.UserId : throw new UnauthorizedAccessException("Only an authenticated Admin can convert report templates.");
    private static void ValidateSource(ReportTemplateSource source) { if (source.PdfBytes.Length == 0) throw new InvalidOperationException("The PDF source is empty."); if (!source.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The template source must be a PDF file."); }
    private static void ValidateDocx(byte[] bytes) { if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B) throw new InvalidOperationException("The conversion did not produce a valid Word document."); }
    private static string BoundError(Exception ex) => ex.ToString().Length > 1024 ? ex.ToString()[..1024] : ex.ToString();
    private async Task<ReportTemplate> PublishAsync(ReportTemplate template, ReportTemplateDocument document, ReportTemplateAuditEntry audit, CancellationToken token, int expectedVersion = 0)
    {
        var publisher = _templates.GetType().GetMethod("PublishAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, binder: null, new[] { typeof(ReportTemplate), typeof(int), typeof(ReportTemplateAuditEntry), typeof(ReportTemplateDocument), typeof(CancellationToken) }, modifiers: null);
        if (publisher is not null)
        {
            var task = (Task<ReportTemplate>)publisher.Invoke(_templates, new object[] { template, expectedVersion, audit, document, token })!;
            return await task;
        }
        return await PublishFallbackAsync(template, document, audit, token, expectedVersion == 0 ? null : expectedVersion);
    }
    private async Task<ReportTemplate> PublishFallbackAsync(ReportTemplate template, ReportTemplateDocument document, ReportTemplateAuditEntry audit, CancellationToken token, int? expectedDocumentVersion = null)
    {
        await _documents.SaveAsync(document, expectedDocumentVersion, token);
        return await _templates.SaveAsync(template, expectedDocumentVersion ?? 0, audit, token);
    }
    private async Task RecordAuditAsync(TemplateConversionAuditEntry entry, CancellationToken token) { try { await _documents.RecordConversionAuditAsync(entry, token); } catch (Exception ex) { _logger?.Log(ex, "PDF to Word audit", $"sourceId={entry.SourceId}; templateId={entry.TemplateId}; actorId={entry.ActorId}"); throw new InvalidOperationException("Conversion completed but its audit record could not be stored.", ex); } }
    private async Task RecordAuditSafelyAsync(TemplateConversionAuditEntry entry, CancellationToken token) { try { await _documents.RecordConversionAuditAsync(entry, token); } catch (Exception auditException) { _logger?.Log(auditException, "PDF to Word failed audit", $"sourceId={entry.SourceId}; templateId={entry.TemplateId}; actorId={entry.ActorId}"); } }
}
