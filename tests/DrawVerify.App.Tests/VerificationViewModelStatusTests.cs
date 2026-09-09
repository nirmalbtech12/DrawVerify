using DrawVerify.App.ViewModels;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using System.ComponentModel;
using System.Reflection;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class VerificationViewModelStatusTests
{
    [Fact(DisplayName = "TD-004 failed refresh state is explicitly empty")]
    public void Failed_refresh_clear_contract_has_no_stale_summary()
    {
        var vm = CreateViewModel(new[] { Match(DimensionType.Linear, DimensionMatchStatus.Matched, 1) });
        vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(1, vm.TotalDimensions);
        SetResults(vm, Array.Empty<DimensionMatch>());
        vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(0, vm.TotalDimensions);
        Assert.Null(vm.MatchPercentage);
        Assert.Equal("N/A (no dimensions)", vm.MatchPercentageText);
    }

    [Fact(DisplayName = "TD-003 exposes session-wide summary independent of status filtering")]
    public void Comparison_summary_includes_all_statuses_and_missing_rows_before_filtering()
    {
        var vm = CreateViewModel(new[]
        {
            Match(DimensionType.Linear, DimensionMatchStatus.Matched, 1),
            Match(DimensionType.Linear, DimensionMatchStatus.Mismatched, 2),
            Match(DimensionType.Angular, DimensionMatchStatus.Unmatched, 3),
            Match(DimensionType.Radial, DimensionMatchStatus.MissingDimension, -1)
        });
        vm.ApplyFilterCommand.Execute(null);

        Assert.Equal(3, vm.TotalDimensions);
        Assert.Equal(1, vm.MatchedDimensions);
        Assert.Equal(1, vm.MismatchedDimensions);
        Assert.Equal(1, vm.UnmatchedDimensions);
        Assert.Equal(1, vm.MissingDimensions);
        Assert.Equal(33.333333333333336, vm.MatchPercentage);
        Assert.Equal("1/3 Matched", vm.MatchRatio);

        vm.StatusFilter = "Matched";
        vm.ApplyFilterCommand.Execute(null);
        Assert.Single(vm.MatchResults);
        Assert.Equal(3, vm.TotalDimensions);
        Assert.Equal((1, 1, 0), Counts(vm, DimensionType.Linear));
        Assert.Equal((0, 0, 1), Counts(vm, DimensionType.Angular));
    }

    [Fact(DisplayName = "TD-010 aggregates a 500-row session in memory")]
    public void Five_hundred_rows_produce_correct_summary_without_extra_data_source_calls()
    {
        var rows = Enumerable.Range(1, 500).Select(i => Match(i % 3 == 0 ? DimensionType.Radial : i % 2 == 0 ? DimensionType.Angular : DimensionType.Linear,
            i % 5 == 0 ? DimensionMatchStatus.Matched : i % 2 == 0 ? DimensionMatchStatus.Mismatched : DimensionMatchStatus.Unmatched, i)).ToArray();
        var vm = CreateViewModel(rows);
        vm.ApplyFilterCommand.Execute(null);

        Assert.Equal(500, vm.TotalDimensions);
        Assert.Equal(rows.Count(row => row.Status == DimensionMatchStatus.Matched), vm.MatchedDimensions);
        Assert.Equal(rows.Count(row => row.Status == DimensionMatchStatus.Mismatched), vm.MismatchedDimensions);
        Assert.Equal(rows.Count(row => row.Status == DimensionMatchStatus.Unmatched), vm.UnmatchedDimensions);
        Assert.Equal(500, vm.MatchResults.Count);
    }

    [Fact(DisplayName = "TD-APP-002 derives exact status summaries by dimension type")]
    public void Status_summaries_are_derived_from_individual_matches()
    {
        var vm = CreateViewModel(Enumerable.Repeat(Match(DimensionType.Linear, DimensionMatchStatus.Matched), 45)
            .Concat(Enumerable.Repeat(Match(DimensionType.Linear, DimensionMatchStatus.Mismatched), 3))
            .Concat(Enumerable.Repeat(Match(DimensionType.Linear, DimensionMatchStatus.Unmatched), 2))
            .Concat(Enumerable.Repeat(Match(DimensionType.Angular, DimensionMatchStatus.Matched), 10))
            .Append(Match(DimensionType.Angular, DimensionMatchStatus.Mismatched))
            .Concat(Enumerable.Repeat(Match(DimensionType.Radial, DimensionMatchStatus.Matched), 20))
            .Concat(Enumerable.Repeat(Match(DimensionType.Radial, DimensionMatchStatus.Unmatched), 5)));

        vm.ApplyFilterCommand.Execute(null);
        Assert.Equal((45, 3, 2), Counts(vm, DimensionType.Linear));
        Assert.Equal((10, 1, 0), Counts(vm, DimensionType.Angular));
        Assert.Equal((20, 0, 5), Counts(vm, DimensionType.Radial));
    }

    [Fact(DisplayName = "TD-APP-003 filters exact status membership and refreshes after replacement")]
    public void Status_filters_and_replacement_clear_stale_rows()
    {
        var first = new[] { Match(DimensionType.Linear, DimensionMatchStatus.Matched, 1), Match(DimensionType.Linear, DimensionMatchStatus.Mismatched, 2), Match(DimensionType.Radial, DimensionMatchStatus.Unmatched, 3) };
        var vm = CreateViewModel(first);
        var notifications = new List<string>(); vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName!);

        foreach (var filter in new[] { "Matched", "Mismatched", "Unmatched" })
        {
            vm.StatusFilter = filter; vm.ApplyFilterCommand.Execute(null);
            Assert.All(vm.MatchResults, item => Assert.Equal(filter, item.StatusLabel));
            Assert.Single(vm.MatchResults);
        }

        SetResults(vm, new[] { Match(DimensionType.Angular, DimensionMatchStatus.Matched, 9), Match(DimensionType.Angular, DimensionMatchStatus.Unmatched, 10) });
        vm.StatusFilter = "All"; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(new[] { 9, 10 }, vm.MatchResults.Select(item => item.Match.DxfDimensionReference));
        Assert.DoesNotContain(vm.MatchResults, item => item.Match.DxfDimensionReference is 1 or 2 or 3);
        Assert.Contains(nameof(vm.MatchResults), notifications);
        Assert.Contains(nameof(vm.StatusSummaries), notifications);
    }

    [Fact(DisplayName = "TD-APP-004 sorts statuses by approved priority with stable ties")]
    public void Status_sorting_is_priority_ordered_and_stable()
    {
        var vm = CreateViewModel(new[]
        {
            Match(DimensionType.Linear, DimensionMatchStatus.Matched, 1),
            Match(DimensionType.Linear, DimensionMatchStatus.Unmatched, 2),
            Match(DimensionType.Linear, DimensionMatchStatus.Mismatched, 3),
            Match(DimensionType.Linear, DimensionMatchStatus.Mismatched, 4),
            Match(DimensionType.Linear, DimensionMatchStatus.Unmatched, 5),
            Match(DimensionType.Linear, DimensionMatchStatus.Matched, 6)
        });
        vm.SortMode = "Status"; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(new[] { 3, 4, 2, 5, 1, 6 }, vm.MatchResults.Select(item => item.Match.DxfDimensionReference));
        vm.SortMode = "Default"; vm.ApplyFilterCommand.Execute(null);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, vm.MatchResults.Select(item => item.Match.DxfDimensionReference));
    }

    private static VerificationViewModel CreateViewModel(IEnumerable<DimensionMatch> matches)
    {
        var vm = new VerificationViewModel(new SessionAuthenticationService(), new RoleAuthorization());
        SetResults(vm, matches);
        return vm;
    }

    private static void SetResults(VerificationViewModel vm, IEnumerable<DimensionMatch> matches) => typeof(VerificationViewModel).GetField("_allMatchResults", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, matches.Select(match => new DimensionMatchItemViewModel(match)).ToArray());
    private static DimensionMatch Match(DimensionType type, DimensionMatchStatus status, int reference = 0) => new() { DimensionType = type, Status = status, DxfDimensionReference = reference == 0 ? Random.Shared.Next() : reference };
    private static (int, int, int) Counts(VerificationViewModel vm, DimensionType type) { var summary = Assert.Single(vm.StatusSummaries.Where(item => item.DimensionType == type)); return (summary.MatchedCount, summary.MismatchedCount, summary.UnmatchedCount); }
}
