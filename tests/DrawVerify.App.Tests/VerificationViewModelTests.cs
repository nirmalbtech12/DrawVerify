using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using Xunit;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using System.Reflection;

namespace DrawVerify.App.Tests;

public sealed class VerificationViewModelTests
{
    [Theory(DisplayName = "TD-APPUI-002 review guard follows Checker-or-Approver authorization")]
    [InlineData("Checker", true)]
    [InlineData("Approver", true)]
    [InlineData("Viewer", false)]
    [InlineData("Maker", false)]
    public void Review_guard_returns_behavioral_result(string role, bool expected)
    {
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("USER", new[] { role }));
        var vm = new VerificationViewModel(session, new RoleAuthorization());
        Assert.Equal(expected, vm.CanReview); Assert.Equal(expected, vm.CanEnterReview);
        var entered = vm.TryEnterReview(out var message); Assert.Equal(expected, entered);
        if (expected) Assert.Empty(message); else Assert.Contains("Checker or Approver", message);
    }

    [Theory]
    [InlineData("Maker", true)]
    [InlineData("Checker", true)]
    [InlineData("Approver", false)]
    [InlineData("Viewer", false)]
    public void Matching_permission_is_limited_to_maker_and_checker(string role, bool expected)
    {
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("USER", new[] { role }));
        var vm = new VerificationViewModel(session, new RoleAuthorization(), null);
        Assert.False(vm.CanMatchDimensions);
        Assert.False(vm.CanViewDimensionMatches);
        Assert.Equal(expected, new RoleAuthorization().IsAllowed(session.CurrentUser, RolePermission.MatchDimensionsToGeometry));
    }

    [Fact]
    public void Result_items_have_distinct_status_labels_and_reasons()
    {
        var version = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        var items = new[]
        {
            new DimensionMatchItemViewModel(new DimensionMatch { VerificationSessionId = sessionId, DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.Matched, Confidence = MatchConfidence.High, StepGeometryReference = "Edge:1" }),
            new DimensionMatchItemViewModel(new DimensionMatch { VerificationSessionId = sessionId, DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.Unmatched, ReasonCode = "NoCorrespondingGeometryFound" }),
            new DimensionMatchItemViewModel(new DimensionMatch { VerificationSessionId = sessionId, DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.MissingDimension })
        };
        Assert.Equal(new[] { "Matched", "Unmatched", "Missing Dimension" }, items.Select(x => x.StatusLabel));
        Assert.Equal("Edge:1", items[0].GeometryReference);
        Assert.Equal("NoCorrespondingGeometryFound", items[1].Reason);
    }

    [Fact]
    public void Unmatched_dimensions_section_exposes_required_metadata_and_filter_controls()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        Assert.Equal("Unmatched Dimensions", vm.UnmatchedDimensionsTitle);
        Assert.Contains("Unmatched", vm.StatusFilters);
        var item = new DimensionMatchItemViewModel(new DimensionMatch
        {
            Status = DimensionMatchStatus.Unmatched, DimensionType = DimensionType.Angular, NominalValue = 45,
            DxfDimensionReference = 8, DimensionText = "45°", ReasonCode = "AmbiguousReference",
            ReasonExplanation = "Ambiguous reference – multiple possible matches."
        });
        Assert.True(item.IsUnmatched);
        Assert.Equal("Angular", item.DimensionType);
        Assert.Equal("45", item.NominalValue);
        Assert.Equal("45°", item.DimensionText);
        Assert.Equal("AmbiguousReference", item.Reason);
        Assert.Contains("multiple possible", item.ReasonExplanation);
    }

    [Fact]
    public void Authorized_view_filters_searches_and_sorts_mixed_results_deterministically()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        var all = new[]
        {
            new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Matched, DimensionType = DimensionType.Linear, NominalValue = 3, DxfDimensionReference = 1 }),
            new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DimensionType = DimensionType.Radial, NominalValue = 9, DxfDimensionReference = 2, DimensionText = "R9", ReasonCode = "NoCorrespondingGeometryFound" }),
            new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DimensionType = DimensionType.Linear, NominalValue = 4, DxfDimensionReference = 3, DimensionText = "4 mm", ReasonCode = "AmbiguousReference" })
        };
        typeof(VerificationViewModel).GetField("_allMatchResults", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, all);
        vm.StatusFilter = "Unmatched"; vm.SearchText = "ambiguous"; vm.ApplyFilterCommand.Execute(null);
        var searched = Assert.Single(vm.MatchResults);
        Assert.Equal(3, searched.Match.DxfDimensionReference);
        vm.SearchText = string.Empty; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(new[] { 3, 2 }, vm.MatchResults.Select(x => x.Match.DxfDimensionReference));
    }

    [Fact]
    public void Report_presentation_exposes_unmatched_heading_and_entries()
    {
        var markdown = new VerificationReport { DimensionMatches = new[] { new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 9, NominalValue = 12, ReasonCode = "DimensionTypeNotSupported" } } }.ToMarkdown();
        Assert.Contains("Unmatched Dimensions – Requires Manual Review", markdown);
        Assert.Contains("Reference: `9`", markdown);
        Assert.Contains("Nominal value: 12", markdown);
        Assert.Contains("DimensionTypeNotSupported", markdown);
    }

    [Fact(DisplayName = "TD-APPUI-003 report workflow generates through the service and clears content on failure")]
    public async Task Report_workflow_generates_and_surfaces_failure()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("maker", new[] { "Maker" }));
        var repository = new ReportRepositoryFake(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 4, NominalValue = 18, ReasonCode = "NoCorrespondingGeometryFound" });
        var service = new ReportService(repository, session, new RoleAuthorization());
        var viewModel = new ReportViewModel(service) { SessionId = repository.SessionId.ToString() };

        Assert.True(await viewModel.GenerateAsync());
        Assert.Contains("Unmatched Dimensions – Requires Manual Review", viewModel.ReportContent);
        Assert.Equal("Report generated.", viewModel.Status);

        repository.ThrowOnRead = true;
        Assert.False(await viewModel.GenerateAsync());
        Assert.Empty(viewModel.ReportContent);
        Assert.StartsWith("Unable to generate report:", viewModel.Status);
    }

    private sealed class ReportRepositoryFake(DimensionMatch match) : IVerificationSessionRepository
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool ThrowOnRead { get; set; }
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => Task.FromResult(new VerificationSession { VerificationSessionId = SessionId, StepDocumentId = stepDocumentId, DxfDocumentId = dxfDocumentId });
        public Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = SessionId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => ThrowOnRead ? Task.FromException<IReadOnlyList<DimensionMatch>>(new InvalidOperationException("repository failure")) : Task.FromResult<IReadOnlyList<DimensionMatch>>(new[] { match });
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

}
