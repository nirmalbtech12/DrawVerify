using System.Text.Json; using DrawVerify.Core.Authorization; using DrawVerify.Core.Models; using DrawVerify.Core.Interfaces;
namespace DrawVerify.Application.Services;
public sealed class ReportTemplateExchangeService : IReportTemplateExchangeService
{
    public const int SchemaVersion = 1;
    private readonly ISessionAuthenticationService? _session; private readonly RoleAuthorization? _authorization; private readonly IReportTemplateAuditRepository? _audit; private readonly ICentralizedLogger? _logger;
    public ReportTemplateExchangeService() { }
    public ReportTemplateExchangeService(ISessionAuthenticationService session, RoleAuthorization authorization, IReportTemplateAuditRepository audit, ICentralizedLogger? logger = null) { _session = session; _authorization = authorization; _audit = audit; _logger = logger; }
    public string Export(ReportTemplate template) { Authorize(); ReportTemplateValidator.Validate(template); var result = JsonSerializer.Serialize(new { schemaVersion = SchemaVersion, template }); Record(template, "Exported", "Template configuration exported"); return result; }
    public ReportTemplate Import(string json) { Authorize(); using var document = JsonDocument.Parse(json); if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != SchemaVersion) throw new InvalidOperationException("Unsupported report template schema version."); var template = document.RootElement.GetProperty("template").Deserialize<ReportTemplate>() ?? throw new InvalidOperationException("Template payload is missing."); ReportTemplateValidator.Validate(template); var draft = template with { IsOrganizationDefault = false }; Record(draft, "Imported", "Template configuration imported as draft"); return draft; }
    private void Authorize()
    {
        if (_session is null || _authorization is null || _audit is null || !_session.IsAuthenticated || !_authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration))
        {
            throw new UnauthorizedAccessException("Only an authenticated Admin can exchange report templates.");
        }
    }
    private void Record(ReportTemplate template, string action, string summary)
    {
        var actor = _session?.CurrentUser ?? throw new UnauthorizedAccessException("An authenticated session is required to audit report template exchange.");
        try { _audit!.RecordAuditAsync(new ReportTemplateAuditEntry { TemplateId = template.Id, Version = template.Version, ActorId = actor.UserId, Action = action, Summary = summary }, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { _logger?.Log(ex, "Report template exchange audit", $"Unable to record {action} for templateId={template.Id}."); throw; }
    }
}
