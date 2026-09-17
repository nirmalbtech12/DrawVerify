using System.Text.Json; using DrawVerify.Core.Models; using DrawVerify.Core.Interfaces;
namespace DrawVerify.Application.Services;
public sealed class ReportTemplateExchangeService : IReportTemplateExchangeService
{
    public const int SchemaVersion = 1;
    public string Export(ReportTemplate template) { ReportTemplateValidator.Validate(template); return JsonSerializer.Serialize(new { schemaVersion = SchemaVersion, template }); }
    public ReportTemplate Import(string json) { using var document = JsonDocument.Parse(json); if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != SchemaVersion) throw new InvalidOperationException("Unsupported report template schema version."); var template = document.RootElement.GetProperty("template").Deserialize<ReportTemplate>() ?? throw new InvalidOperationException("Template payload is missing."); ReportTemplateValidator.Validate(template); return template with { IsOrganizationDefault = false }; }
}
