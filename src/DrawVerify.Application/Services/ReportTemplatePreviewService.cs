using DrawVerify.Core.Authorization; using DrawVerify.Core.Interfaces; using DrawVerify.Core.Models;
namespace DrawVerify.Application.Services;
public sealed class ReportTemplatePreviewService : IReportTemplatePreviewService
{
    private readonly IPdfReportGenerator _renderer; private readonly ISessionAuthenticationService? _session; private readonly RoleAuthorization? _authorization; private readonly ICentralizedLogger? _logger;
    public ReportTemplatePreviewService(IPdfReportGenerator renderer) => _renderer = renderer;
    public ReportTemplatePreviewService(IPdfReportGenerator renderer, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger? logger = null) : this(renderer) { _session = session; _authorization = authorization; _logger = logger; }
    public async Task<byte[]> PreviewAsync(ReportTemplate template, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_session is not null && (!_session.IsAuthenticated || !_authorization!.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration))) throw new UnauthorizedAccessException("Only an authenticated Admin can preview report templates.");
            ReportTemplateValidator.Validate(template); cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var report = new VerificationReport
            {
                Metadata = new VerificationReportMetadata { DocumentName = "Sample verification", GeneratedUtc = now, TemplateId = template.Id, TemplateVersion = template.Version },
                DimensionMatches = [
                    new DimensionMatch { VerificationSessionId = sessionId, DxfDimensionReference = 1, NominalValue = 10, MeasuredValue = 10, EffectiveLowerTolerance = 9.9, EffectiveUpperTolerance = 10.1, Status = DrawVerify.Core.Enums.DimensionMatchStatus.Matched },
                    new DimensionMatch { VerificationSessionId = sessionId, DxfDimensionReference = 2, NominalValue = 20, MeasuredValue = 20.4, EffectiveLowerTolerance = 19.9, EffectiveUpperTolerance = 20.1, Status = DrawVerify.Core.Enums.DimensionMatchStatus.Mismatched, ReasonCode = "Outside tolerance", RequiresManualReview = true },
                    new DimensionMatch { VerificationSessionId = sessionId, DxfDimensionReference = 3, NominalValue = 30, EffectiveLowerTolerance = 29.8, EffectiveUpperTolerance = 30.2, Status = DrawVerify.Core.Enums.DimensionMatchStatus.Unmatched, ReasonCode = "Not found" }
                ],
                GdtResults = [new GdtResult { VerificationSessionId = sessionId, Identifier = "FCF-01", ControlType = DrawVerify.Core.Enums.GdtControlType.Position, DatumReferences = ["A", "B", "C"], ToleranceValue = "0.10", Status = DrawVerify.Core.Enums.VerificationStatus.Passed, ComparisonDetails = "Within tolerance" }],
                AuditTrail = [new AuditTrailEntry { UserId = "sample-admin", UserName = "Sample Admin", UserRole = "Admin", Details = "Sample audit entry", Timestamp = now }]
            };
            var pdf = await _renderer.GeneratePdfAsync(report, template, cancellationToken).ConfigureAwait(false);
            if (pdf is null || pdf.Length == 0)
            {
                throw new InvalidOperationException($"Preview renderer returned an empty PDF for templateId={template.Id}, version={template.Version}.");
            }

            return pdf;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not OperationCanceledException) { _logger?.Log(ex, "Report template preview", $"Unable to preview templateId={template.Id}, version={template.Version}."); throw; }
    }
}
