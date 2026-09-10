using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ReportViewModelTests
{
    [Fact(DisplayName = "TD-REPORT-001 report ViewModel exposes extra dimensions in preview")]
    public async Task Report_preview_includes_extra_dimension_section()
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker" });
        var sessionId = Guid.NewGuid();
        var viewModel = new ReportViewModel(new ReportService(new ReportRepository(sessionId, new[] { new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:extra", StepFeatureValue = 7.5 } }), authentication)) { SessionId = sessionId.ToString() };

        Assert.True(await viewModel.GenerateAsync());
        Assert.Contains("Extra Dimensions", viewModel.ReportContent);
        Assert.Contains("Edge:extra", viewModel.ReportContent);
    }
    [Fact(DisplayName = "TD-008 CSV export command surfaces authorized export content")]
    public async Task Csv_export_updates_content_and_status()
    {
        var authentication = new SessionAuthenticationService(); authentication.SignIn(new AuthenticatedUser("approver", new[] { SystemRoles.Approver }) { UserId = "approver" });
        var sessionId = Guid.NewGuid(); var service = new ReportService(new ReportRepository(sessionId), authentication, new RoleAuthorization()); var viewModel = new ReportViewModel(service) { SessionId = sessionId.ToString() };
        Assert.True(await viewModel.ExportMissingDimensionsAsync()); Assert.Contains("Layer", viewModel.CsvContent); Assert.Contains("DIM", viewModel.CsvContent); Assert.Equal("Missing dimensions exported.", viewModel.Status);
    }

    private sealed class ReportRepository(Guid sessionId, IReadOnlyList<DimensionMatch>? matches = null) : IVerificationSessionRepository
    {
        private readonly IReadOnlyList<DimensionMatch> _matches = matches ?? new[] { new DimensionMatch { DimensionMatchId = Guid.NewGuid(), Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 1, NominalValue = 50, DxfLayer = "DIM", DimensionText = "50.00" } };
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = sessionId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? _matches : _matches.Where(item => item.Status == status).ToArray());
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
