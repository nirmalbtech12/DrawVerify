using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using DrawVerify.Application.Services;
using Xunit;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Configuration;
using System.Reflection;
using DrawVerify.Geometry.Models;
using DrawVerify.Geometry;
using CoreDimensionData = DrawVerify.Core.Models.DimensionData;

namespace DrawVerify.App.Tests;

public sealed class VerificationViewModelTests
{
    private static void SetResults(VerificationViewModel vm, IEnumerable<DimensionMatch> matches) =>
        typeof(VerificationViewModel).GetField("_allMatchResults", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, matches.Select(match => new DimensionMatchItemViewModel(match)).ToArray());

    [Fact(DisplayName = "TD-UI-001 displays extras in a separate collection with summary counts")]
    public void Extra_rows_are_separate_from_missing_rows_and_summary_counts_are_distinct()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        SetResults(vm, new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DimensionType = DimensionType.Linear },
            new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DimensionType = DimensionType.Linear },
            new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, DimensionType = DimensionType.Radial, StepGeometryReference = "Edge:extra" }
        });
        vm.ApplyFilterCommand.Execute(null);

        Assert.Single(vm.UnmatchedResults);
        Assert.Single(vm.ExtraDimensionResults);
        Assert.Equal(1, vm.MissingIn3DCount);
        Assert.Equal(1, vm.ExtraDimensionCount);
    }

    [Fact(DisplayName = "TD-UI-002 selecting an extra row highlights and targets the STEP feature")]
    public void Extra_selection_updates_step_preview_target()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        var item = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.ExtraDimension, StepGeometryReference = "Edge:extra", StepLocationX = 4, StepLocationY = 5, StepLocationZ = 6 });
        vm.SelectedExtraDimension = item;

        Assert.Equal("Edge:extra", vm.StepPreview.SelectedFeatureReference);
        Assert.Equal((4d, 5d, 6d), (vm.StepPreview.TargetX, vm.StepPreview.TargetY, vm.StepPreview.TargetZ));
        Assert.True(vm.StepPreview.IsFeatureHighlighted);
        Assert.Contains("Highlighted", vm.StepPreview.SelectionStatus);
    }

    [Fact(DisplayName = "TD-UI-002 reports unavailable STEP preview location explicitly")]
    public void Extra_selection_without_location_does_not_claim_highlight()
    {
        var preview = new StepPreviewViewModel();
        preview.SelectFeature("Face:missing-location", null, 2, 3);

        Assert.False(preview.IsFeatureHighlighted);
        Assert.False(preview.IsLocationAvailable);
        Assert.Contains("no valid preview location", preview.SelectionStatus);
    }

    [Fact(DisplayName = "TD-UI-002 failed refresh clears stale extra results and STEP preview selection")]
    public async Task Failed_refresh_clears_extra_results_and_preview_state()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("checker", new[] { "Checker" }));
        var sessionId = Guid.NewGuid();
        var repository = new RefreshFailureRepository(sessionId);
        var matching = new DimensionMatchingService(new EmptyDocuments(), repository, new EmptyGeometry(), session, new DimensionMatchingConfiguration());
        var viewModel = new VerificationViewModel(session, new RoleAuthorization(), matching);
        typeof(VerificationViewModel).GetField("<SelectedSessionId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, sessionId);

        await viewModel.RefreshMatchesAsync(sessionId);
        var extra = Assert.Single(viewModel.ExtraDimensionResults);
        viewModel.SelectedExtraDimension = extra;
        Assert.True(viewModel.StepPreview.IsFeatureHighlighted);

        repository.ThrowOnRead = true;
        await viewModel.RefreshMatchesAsync(sessionId);

        Assert.Empty(viewModel.ExtraDimensionResults);
        Assert.Null(viewModel.SelectedExtraDimension);
        Assert.Null(viewModel.StepPreview.SelectedFeatureReference);
        Assert.False(viewModel.StepPreview.IsFeatureHighlighted);
        Assert.Null(viewModel.StepPreview.TargetX);
        Assert.Null(viewModel.StepPreview.TargetY);
        Assert.Null(viewModel.StepPreview.TargetZ);
    }

    [Theory(DisplayName = "TD-UI-003 exposes extra-dimension mutation only to Checker and Approver")]
    [InlineData("Checker", true)]
    [InlineData("Approver", true)]
    [InlineData("Viewer", false)]
    [InlineData("Maker", false)]
    public void Extra_review_commands_follow_role_permissions(string role, bool canMutate)
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("user", new[] { role }));
        var vm = new VerificationViewModel(session, new RoleAuthorization(), new DimensionMatchingService(new EmptyDocuments(), new EmptySessions(), new EmptyGeometry(), session, new DimensionMatchingConfiguration()));

        Assert.True(vm.CanViewExtraDimensions);
        if (canMutate)
        {
            Assert.True(vm.CanCommentExtraDimensions);
            Assert.True(vm.CanResolveExtraDimensions);
        }
        else
        {
            Assert.False(vm.CanCommentExtraDimensions);
            Assert.False(vm.CanResolveExtraDimensions);
        }
    }

    [Fact(DisplayName = "TD-003 filtered result grid does not change overall summary")]
    public void Filtered_rows_do_not_change_summary_values()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        SetResults(vm, new[]
        {
            new DimensionMatch { DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Matched },
            new DimensionMatch { DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Unmatched }
        });
        vm.ApplyFilterCommand.Execute(null);
        vm.StatusFilter = "Matched"; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(2, vm.TotalDimensions);
        Assert.Single(vm.MatchResults);
    }

    [Fact(DisplayName = "TD-010 500-row summary is exposed through ViewModel")]
    public void Five_hundred_rows_have_aggregate_summary_fields()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        SetResults(vm, Enumerable.Range(1, 500).Select(i => new DimensionMatchItemViewModel(new DimensionMatch
        {
            DimensionType = DimensionType.Linear,
            Status = i % 2 == 0 ? DimensionMatchStatus.Matched : DimensionMatchStatus.Mismatched,
            DxfDimensionReference = i
        }).Match));
        vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(500, vm.TotalDimensions);
        Assert.Equal(250, vm.MatchedDimensions);
        Assert.Equal(250, vm.MismatchedDimensions);
    }

    [Fact(DisplayName = "TD-APP-004 refreshed results expose the replacement delta")]
    public void Refreshed_match_item_uses_current_persisted_measurement()
    {
        var item = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 10.275 });
        Assert.Equal(-.275, item.Match.SignedDelta!.Value, 12); Assert.Equal(.275, item.DeltaMagnitude!.Value, 12); Assert.Equal("Δ = -0.275 mm", item.SignedDelta);
    }
    [Fact(DisplayName = "TD-APPUI-001 formats signed and absolute deltas with invariant precision")]
    public void Delta_values_use_explicit_sign_three_decimals_and_units()
    {
        var item = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 9.85 });
        var negative = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Radial, NominalValue = 9.92, MeasuredValue = 10 });
        var angle = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Angular, NominalValue = 45, MeasuredValue = 42.5 });
        Assert.Equal("Δ = +0.150 mm", item.SignedDelta); Assert.Equal("Δ = 0.150 mm", item.AbsoluteDelta);
        Assert.Equal("Δ = -0.080 mm", negative.SignedDelta); Assert.Equal("Δ = 0.080 mm", negative.AbsoluteDelta);
        Assert.Equal("Δ = +2.500 °", angle.SignedDelta); Assert.Equal("°", angle.DeltaUnit);
    }

    [Fact(DisplayName = "TD-APPUI-002 applies tolerance-based delta severity and handles invalid values")]
    public void Delta_styling_uses_unrounded_effective_tolerance()
    {
        var significant = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, NominalValue = 10, MeasuredValue = 9.4, EffectiveLowerTolerance = -.5, EffectiveUpperTolerance = .5 });
        var moderate = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, NominalValue = 10, MeasuredValue = 9.6, EffectiveLowerTolerance = -.5, EffectiveUpperTolerance = .5 });
        var invalid = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, NominalValue = double.NaN, MeasuredValue = 1, EffectiveLowerTolerance = -.5, EffectiveUpperTolerance = .5 });
        var matched = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Matched, NominalValue = 10, MeasuredValue = 9 });
        Assert.Equal(System.Windows.Media.Brushes.Red, significant.DeltaBrush); Assert.Equal(System.Windows.FontWeights.Bold, significant.DeltaFontWeight);
        Assert.Equal(System.Windows.Media.Brushes.DarkOrange, moderate.DeltaBrush); Assert.Equal(System.Windows.FontWeights.Normal, moderate.DeltaFontWeight);
        Assert.Equal("-", invalid.SignedDelta); Assert.Equal(System.Windows.Media.Brushes.Gray, invalid.DeltaBrush); Assert.Equal(System.Windows.Media.Brushes.Gray, matched.DeltaBrush);
    }

    [Fact(DisplayName = "TD-APPUI-003 sorts numeric delta magnitude while preserving filters")]
    public void Delta_sort_is_numeric_with_nulls_last_and_filters_preserved()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        var all = new[]
        {
            new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 9.5, DxfDimensionReference = 1, DimensionText = "large" }),
            new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Linear, NominalValue = 10, MeasuredValue = 9.85, DxfDimensionReference = 2, DimensionText = "small" }),
            new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 3, DimensionText = "none" })
        };
        typeof(VerificationViewModel).GetField("_allMatchResults", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, all);
        vm.SortMode = "Delta magnitude"; vm.SortDescending = true; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(new[] { 1, 2, 3 }, vm.MatchResults.Select(x => x.Match.DxfDimensionReference));
        vm.StatusFilter = "Mismatched"; vm.SortDescending = false; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(new[] { 2, 1 }, vm.MatchResults.Select(x => x.Match.DxfDimensionReference));
    }

    [Fact(DisplayName = "TD-APPUI-004 handles approximately 500 display, sort, and filter results")]
    public void Five_hundred_delta_items_are_processed_in_memory()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        var all = Enumerable.Range(1, 500).Select(i => new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, DimensionType = DimensionType.Linear, NominalValue = i, MeasuredValue = i - (i % 7) / 10.0, DxfDimensionReference = i })).ToArray();
        typeof(VerificationViewModel).GetField("_allMatchResults", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, all);
        vm.SortMode = "Delta magnitude"; vm.SortDescending = true; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(500, vm.MatchResults.Count); Assert.True(vm.MatchResults.First().DeltaMagnitude >= vm.MatchResults.Last().DeltaMagnitude);
    }

    [Fact(DisplayName = "TD-004 clears summary and prior rows after authorization failure")]
    public async Task Unauthorized_refresh_clears_existing_delta_rows()
    {
        var session = new SessionAuthenticationService();
        var vm = new VerificationViewModel(session, new RoleAuthorization(), new DimensionMatchingService(new EmptyDocuments(), new EmptySessions(), new EmptyGeometry(), session, new DimensionMatchingConfiguration()));
        var item = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Mismatched, NominalValue = 2, MeasuredValue = 1 });
        typeof(VerificationViewModel).GetField("_allMatchResults", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, new[] { item });
        vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(1, vm.TotalDimensions);
        await vm.RefreshMatchesAsync(Guid.NewGuid());
        Assert.Empty(vm.MatchResults); Assert.Equal("Access denied.", vm.MatchingStatus);
        Assert.Equal(0, vm.TotalDimensions); Assert.Equal(0, vm.MissingDimensions); Assert.Null(vm.MatchPercentage); Assert.Equal("N/A (no dimensions)", vm.MatchPercentageText);
    }

    [Fact(DisplayName = "TD-009 Checker and Approver receive identical summary contract")]
    public void Approver_is_authorized_for_the_same_summary_surface_as_checker()
    {
        var authorization = new RoleAuthorization();
        var checker = new AuthenticatedUser("checker", new[] { "Checker" });
        var approver = new AuthenticatedUser("approver", new[] { "Approver" });
        Assert.True(authorization.IsAllowed(checker, RolePermission.ViewDimensionMatchResults));
        Assert.True(authorization.IsAllowed(approver, RolePermission.ViewDimensionMatchResults));

        var rows = new[]
        {
            new DimensionMatch { DimensionType = DimensionType.Linear, Status = DimensionMatchStatus.Matched },
            new DimensionMatch { DimensionType = DimensionType.Angular, Status = DimensionMatchStatus.Mismatched },
            new DimensionMatch { DimensionType = DimensionType.Radial, Status = DimensionMatchStatus.Unmatched }
        };
        var checkerVm = new VerificationViewModel(new SessionAuthenticationService(), authorization);
        var approverVm = new VerificationViewModel(new SessionAuthenticationService(), authorization);
        SetResults(checkerVm, rows); SetResults(approverVm, rows);
        checkerVm.ApplyFilterCommand.Execute(null); approverVm.ApplyFilterCommand.Execute(null);
        Assert.Equal(checkerVm.TotalDimensions, approverVm.TotalDimensions);
        Assert.Equal(checkerVm.MatchedDimensions, approverVm.MatchedDimensions);
        Assert.Equal(checkerVm.MismatchedDimensions, approverVm.MismatchedDimensions);
        Assert.Equal(checkerVm.UnmatchedDimensions, approverVm.UnmatchedDimensions);
        Assert.Equal(checkerVm.DimensionTypeSummaries, approverVm.DimensionTypeSummaries);
    }
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

    [Fact(DisplayName = "TD-APPUI-001 exposes distinct unmatched presentation and reasons")]
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
        Assert.Equal("Green", items[0].StatusColor); Assert.Equal("Gray", items[1].StatusColor);
        Assert.Equal("Edge:1", items[0].GeometryReference);
        Assert.Equal("NoCorrespondingGeometryFound", items[1].Reason);
    }

    [Fact(DisplayName = "TD-UI-001 presents tolerance evidence, units, and matched/mismatched colors")]
    public void Tolerance_result_presentation_is_visible()
    {
        var version = Guid.NewGuid();
        var matched = new DimensionMatchItemViewModel(new DimensionMatch { DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.Matched, MeasuredValue = 10.05, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .1, ToleranceUnit = "mm", ToleranceSource = ToleranceSource.Default });
        var mismatched = new DimensionMatchItemViewModel(new DimensionMatch { DxfDocumentVersionId = version, StepDocumentVersionId = version, Status = DimensionMatchStatus.Mismatched, MeasuredValue = 10.2, EffectiveLowerTolerance = -.1, EffectiveUpperTolerance = .1, ToleranceUnit = "mm", ToleranceSource = ToleranceSource.Session, ReasonCode = "OutsideTolerance" });
        Assert.Equal("10.05", matched.MeasuredValue); Assert.Contains("mm", matched.Tolerance); Assert.Equal("Default", matched.ToleranceSource); Assert.Equal("Green", matched.StatusColor);
        Assert.Equal("Mismatched", mismatched.StatusLabel); Assert.Equal("Red", mismatched.StatusColor); Assert.Equal("Session", mismatched.ToleranceSource);
        Assert.Contains("Tolerance: ±0.1 mm", new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization()).ToleranceSummary);
    }

    [Theory(DisplayName = "TD-UI-001 only Maker and Checker can manage tolerance")]
    [InlineData(null, false)] [InlineData("Viewer", false)] [InlineData("Approver", false)] [InlineData("Maker", true)] [InlineData("Checker", true)]
    public void Tolerance_management_permission_is_role_restricted(string? role, bool expected)
    {
        var session = new SessionAuthenticationService(); if (role is not null) session.SignIn(new AuthenticatedUser("user", new[] { role }));
        var vm = new VerificationViewModel(session, new RoleAuthorization(), new DimensionMatchingService(new EmptyDocuments(), new EmptySessions(), new EmptyGeometry(), session, new DimensionMatchingConfiguration()));
        Assert.Equal(expected, vm.CanManageTolerance);
    }

    [Fact(DisplayName = "TD-APPUI-001 exposes the Unmatched Dimensions list and required metadata")]
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

    [Fact(DisplayName = "TD-07 selecting an unmatched item requests DXF highlight and navigation")]
    public void Selecting_unmatched_item_updates_preview_identity_and_status()
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        vm.SelectedUnmatchedDimension = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 22, PreviewAnchor = "1,2" });
        Assert.Equal("22", vm.DxfPreview.SelectedEntityReference); Assert.Equal("1,2", vm.DxfPreview.SelectedAnchor); Assert.True(vm.DxfPreview.IsHighlightVisible); Assert.Contains("highlighted", vm.DxfPreview.PreviewStatus);
        vm.SelectedUnmatchedDimension = new DimensionMatchItemViewModel(new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 23 });
        Assert.False(vm.DxfPreview.IsHighlightVisible); Assert.Contains("unavailable", vm.DxfPreview.PreviewStatus);
    }

    [Theory(DisplayName = "TD-08 exposes missing summary counts without counting STEP-extra rows")]
    [InlineData(0, 0)] [InlineData(2, 1)]
    public void Summary_distinguishes_unmatched_and_extra_step_rows(int unmatched, int extra)
    {
        var rows = Enumerable.Range(1, unmatched).Select(i => new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = i }).Concat(Enumerable.Range(1, extra).Select(i => new DimensionMatch { Status = DimensionMatchStatus.MissingDimension, DxfDimensionReference = -i }));
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization()); SetResults(vm, rows); vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(unmatched, vm.UnmatchedDimensions); Assert.Equal(extra, vm.MissingDimensions); Assert.Equal(unmatched, vm.TotalDimensions);
    }

    [Theory(DisplayName = "TD-06 and TD-08 role matrix permits only the approved missing-dimension actions")]
    [InlineData("Maker", true, true, false)] [InlineData("Checker", true, true, true)] [InlineData("Approver", true, true, true)] [InlineData("Viewer", true, false, false)]
    public void Missing_dimension_permissions_follow_role_matrix(string role, bool canView, bool canComment, bool canResolve)
    {
        var auth = new RoleAuthorization(); var user = new AuthenticatedUser(role, new[] { role });
        Assert.Equal(canView, auth.IsAllowed(user, RolePermission.ViewMissingDimensions)); Assert.Equal(canComment, auth.IsAllowed(user, RolePermission.CommentOnMissingDimensions)); Assert.Equal(canResolve, auth.IsAllowed(user, RolePermission.ResolveUnmatched));
    }

    [Fact(DisplayName = "TD-REVIEW-02 Viewer refresh loads unmatched rows through the missing-dimension path")]
    public async Task Viewer_refresh_uses_missing_results_and_disables_mutations()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("viewer", new[] { "Viewer" }));
        var sessionId = Guid.NewGuid();
        var repository = new ViewerSessionRepository(sessionId, new DimensionMatch
        {
            DimensionMatchId = Guid.NewGuid(), VerificationSessionId = sessionId,
            DxfDocumentVersionId = Guid.NewGuid(), DxfDimensionReference = 17,
            Status = DimensionMatchStatus.Unmatched, ReasonCode = "NoCorrespondingGeometryFound"
        });
        var matching = new DimensionMatchingService(new EmptyDocuments(), repository, new EmptyGeometry(), session, new DimensionMatchingConfiguration());
        var viewModel = new VerificationViewModel(session, new RoleAuthorization(), matching);
        typeof(VerificationViewModel).GetField("<SelectedSessionId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, sessionId);

        await viewModel.RefreshMatchesAsync(sessionId);

        var result = Assert.Single(viewModel.UnmatchedResults);
        Assert.Equal(17, result.Match.DxfDimensionReference);
        Assert.Equal(1, repository.MissingResultsCallCount);
        viewModel.SelectedUnmatchedDimension = result;
        Assert.False(viewModel.AddMissingDimensionCommentCommand.CanExecute(null));
        Assert.False(viewModel.ResolveMissingDimensionCommand.CanExecute("Accepted"));
    }

    [Fact(DisplayName = "TD-APPUI-002 filters, searches, and sorts mixed results deterministically")]
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
        Assert.Equal("Unable to generate verification report.", viewModel.Status);
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

    private sealed class EmptyDocuments : IDocumentRepository
    {
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(null);
        public Document? Get(Guid id) => null; public DocumentVersion? GetCurrentVersion(Guid id) => null;
        public Task<IReadOnlyList<CoreDimensionData>> GetDimensionsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CoreDimensionData>>(Array.Empty<CoreDimensionData>());
    }
    private sealed class EmptySessions : IVerificationSessionRepository
    {
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(null);
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DimensionMatch>>(Array.Empty<DimensionMatch>());
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class ViewerSessionRepository(Guid sessionId, DimensionMatch unmatched) : IVerificationSessionRepository
    {
        public int MissingResultsCallCount { get; private set; }
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => Task.FromResult(new VerificationSession { VerificationSessionId = sessionId, StepDocumentId = stepDocumentId, DxfDocumentId = dxfDocumentId });
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = sessionId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default)
        {
            if (status == DimensionMatchStatus.Unmatched) MissingResultsCallCount++;
            IReadOnlyList<DimensionMatch> result = status is null || status == DimensionMatchStatus.Unmatched ? new[] { unmatched } : Array.Empty<DimensionMatch>();
            return Task.FromResult(result);
        }
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class RefreshFailureRepository(Guid sessionId) : IVerificationSessionRepository
    {
        public bool ThrowOnRead { get; set; }
        private readonly DimensionMatch _extra = new() { DimensionMatchId = Guid.NewGuid(), VerificationSessionId = sessionId, Status = DimensionMatchStatus.ExtraDimension, ReasonCode = "NoCorrespondingDimension", StepGeometryReference = "Edge:extra", StepLocationX = 1, StepLocationY = 2, StepLocationZ = 3 };
        public Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default) => Task.FromResult(new VerificationSession { VerificationSessionId = sessionId, StepDocumentId = stepDocumentId, DxfDocumentId = dxfDocumentId });
        public Task<VerificationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VerificationSession?>(new VerificationSession { VerificationSessionId = sessionId });
        public Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid id, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default)
            => ThrowOnRead ? Task.FromException<IReadOnlyList<DimensionMatch>>(new InvalidOperationException("refresh failure")) : Task.FromResult<IReadOnlyList<DimensionMatch>>(status is null ? new[] { _extra } : status == DimensionMatchStatus.ExtraDimension ? new[] { _extra } : Array.Empty<DimensionMatch>());
        public Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class EmptyGeometry : IMatchableGeometryService
    {
        public MatchableGeometryExtractionResult ExtractMatchableGeometry(MatchableGeometryExtractionRequest request, CancellationToken cancellationToken = default) => new(true, Array.Empty<GeometryFeatureData>());
    }

}
