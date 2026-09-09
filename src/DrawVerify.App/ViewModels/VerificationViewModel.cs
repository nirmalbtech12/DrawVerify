using DrawVerify.Core.Authorization;
using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Core.Configuration;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.App.ViewModels;

public sealed class VerificationViewModel : INotifyPropertyChanged
{
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly DimensionMatchingService? _matching;
    private readonly ICentralizedLogger? _logger;
    private readonly double _configuredToleranceThresholdMm;
    private double? _effectiveToleranceThresholdMm;
    public VerificationViewModel(ISessionAuthenticationService session, RoleAuthorization authorization, DimensionMatchingService? matching = null, ICentralizedLogger? logger = null, DimensionMatchingConfiguration? configuration = null) { _session = session; _authorization = authorization; _matching = matching; _logger = logger; _configuredToleranceThresholdMm = configuration?.ToleranceThresholdMm ?? 0.1; ToleranceThresholdInput = FormatTolerance(_configuredToleranceThresholdMm); SelectPairCommand = new RelayCommand(async _ => await SelectPairAsync(), _ => CanMatchDimensions && !IsBusy); MatchDimensionsCommand = new RelayCommand(async _ => await MatchSelectedDimensionsAsync(), _ => CanMatchDimensions && SelectedSessionId is not null && !IsBusy); RefreshMatchesCommand = new RelayCommand(async _ => await RefreshSelectedMatchesAsync(), _ => CanViewDimensionMatches && SelectedSessionId is not null && !IsBusy); UpdateToleranceCommand = new RelayCommand(async _ => await UpdateToleranceAsync(), _ => CanManageTolerance && SelectedSessionId is not null && !IsBusy); ApplyFilterCommand = new RelayCommand(_ => ApplyResultView()); SortResultsCommand = new RelayCommand(_ => ApplyResultView()); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanReview => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ReviewVerification);
    public bool CanEnterReview => CanReview;
    public string DenialMessage => "The Checker or Approver role is required to review verification results.";
    public bool TryEnterReview(out string message) { if (CanReview) { message = string.Empty; return true; } message = DenialMessage; return false; }
    public bool CanMatchDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.MatchDimensionsToGeometry);
    public bool CanViewDimensionMatches => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewDimensionMatchResults);
    public bool CanManageTolerance => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageToleranceComparison);
    public string ToleranceThresholdInput { get; set; } = "0.1";
    public string StepDocumentId { get; set; } = string.Empty;
    public string DxfDocumentId { get; set; } = string.Empty;
    public Guid? SelectedSessionId { get; private set; }
    public bool IsBusy { get; private set; }
    public string StatusFilter { get; set; } = "All";
    public bool SortDescending { get; set; }
    public string SortMode { get; set; } = "Default";
    public IReadOnlyList<string> SortModes { get; } = new[] { "Default", "Delta magnitude" };
    public string SearchText { get; set; } = string.Empty;
    public string UnmatchedDimensionsTitle => "Unmatched Dimensions";
    public IReadOnlyList<string> StatusFilters { get; } = new[] { "All", "Matched", "Manual review", "Unmatched", "Missing Dimension" };
    public ICommand SelectPairCommand { get; }
    public ICommand MatchDimensionsCommand { get; }
    public ICommand RefreshMatchesCommand { get; }
    public ICommand UpdateToleranceCommand { get; }
    public ICommand ApplyFilterCommand { get; }
    public ICommand SortResultsCommand { get; }
    public string MatchingStatus { get; private set; } = "No matching session selected.";
    public string ToleranceSummary => $"Tolerance: ±{FormatTolerance(_effectiveToleranceThresholdMm ?? _configuredToleranceThresholdMm)} mm (configured/session; explicit dimension tolerances override)";
    public IReadOnlyList<DimensionMatchItemViewModel> MatchResults { get; private set; } = Array.Empty<DimensionMatchItemViewModel>();
    private IReadOnlyList<DimensionMatchItemViewModel> _allMatchResults = Array.Empty<DimensionMatchItemViewModel>();
    public async Task<bool> MatchDimensionsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!CanMatchDimensions) { MatchingStatus = "Only authenticated Maker or Checker users may match dimensions."; return false; }
        try { SelectedSessionId = sessionId; var session = await _matching!.MatchDimensionsToGeometryAsync(sessionId, cancellationToken); SetEffectiveTolerance(session.ToleranceThresholdMm ?? _configuredToleranceThresholdMm, updateInput: true); MatchingStatus = session.Status.ToString(); await RefreshMatchesAsync(sessionId, cancellationToken); Notify(nameof(SelectedSessionId)); return true; }
        catch (UnauthorizedAccessException) { MatchResults = Array.Empty<DimensionMatchItemViewModel>(); MatchingStatus = "Access denied."; return false; }
        catch (Exception exception) { MatchResults = Array.Empty<DimensionMatchItemViewModel>(); MatchingStatus = exception.Message; return false; }
    }
    public async Task RefreshMatchesAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!CanViewDimensionMatches) { MatchResults = Array.Empty<DimensionMatchItemViewModel>(); MatchingStatus = "Access denied."; return; }
        try { SetEffectiveTolerance(await _matching!.GetEffectiveToleranceThresholdAsync(sessionId, cancellationToken)); _allMatchResults = (await _matching!.GetResultsAsync(sessionId, cancellationToken: cancellationToken)).Select(match => new DimensionMatchItemViewModel(match)).ToArray(); ApplyResultView(); }
        catch (UnauthorizedAccessException) { ClearResults("Access denied."); }
        catch (Exception exception) { _logger?.Log(exception, "Load dimension matching results", $"Unable to load verificationSessionId={sessionId}."); ClearResults("Unable to load matching results."); }
    }
    private async Task SelectPairAsync()
    {
        if (!Guid.TryParse(StepDocumentId, out var stepId) || !Guid.TryParse(DxfDocumentId, out var dxfId)) { MatchingStatus = "Enter valid STEP and DXF document IDs."; Notify(nameof(MatchingStatus)); return; }
        try { IsBusy = true; Notify(nameof(IsBusy)); var session = await _matching!.GetOrCreateSessionAsync(stepId, dxfId); SelectedSessionId = session.VerificationSessionId; MatchingStatus = $"Selected session {session.VerificationSessionId}"; await RefreshMatchesAsync(session.VerificationSessionId); Notify(nameof(SelectedSessionId)); }
        catch (Exception exception) { MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : exception.Message; MatchResults = Array.Empty<DimensionMatchItemViewModel>(); Notify(nameof(MatchingStatus)); Notify(nameof(MatchResults)); }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    private async Task MatchSelectedDimensionsAsync() { if (SelectedSessionId is Guid id) { IsBusy = true; Notify(nameof(IsBusy)); RaiseCommands(); try { await MatchDimensionsAsync(id); } finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); } } }
    private async Task RefreshSelectedMatchesAsync() { if (SelectedSessionId is Guid id) { IsBusy = true; Notify(nameof(IsBusy)); try { await RefreshMatchesAsync(id); } finally { IsBusy = false; Notify(nameof(IsBusy)); } } }
    private async Task UpdateToleranceAsync() { if (SelectedSessionId is not Guid id || !double.TryParse(ToleranceThresholdInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) || !double.IsFinite(tolerance) || tolerance < 0) { MatchingStatus = "Enter a non-negative tolerance in millimetres."; Notify(nameof(MatchingStatus)); return; } IsBusy = true; Notify(nameof(IsBusy)); try { var session = await _matching!.UpdateToleranceAndRecompareAsync(id, tolerance); SetEffectiveTolerance(session.ToleranceThresholdMm ?? tolerance, updateInput: true); await RefreshMatchesAsync(id); MatchingStatus = $"Tolerance comparison completed using ±{FormatTolerance(tolerance)} mm."; Notify(nameof(MatchingStatus)); } catch (Exception exception) { _logger?.Log(exception, "Tolerance re-comparison", $"Unable to recompare verificationSessionId={id}."); MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : exception.Message; Notify(nameof(MatchingStatus)); } finally { IsBusy = false; RaiseCommands(); } }
    private void ApplyResultView()
    {
        var filtered = MatchResults;
        filtered = _allMatchResults;
        if (!string.Equals(StatusFilter, "All", StringComparison.OrdinalIgnoreCase)) filtered = filtered.Where(item => string.Equals(item.StatusLabel, StatusFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!string.IsNullOrWhiteSpace(SearchText)) filtered = filtered.Where(item => string.Join(" ", item.DimensionText, item.NominalValue, item.DimensionType, item.Reason, item.ReasonExplanation, item.Match.DxfDimensionReference).Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (string.Equals(SortMode, "Delta magnitude", StringComparison.OrdinalIgnoreCase))
        {
            var withNullsLast = filtered.OrderBy(item => item.DeltaMagnitude is null);
            MatchResults = (SortDescending ? withNullsLast.ThenByDescending(item => item.DeltaMagnitude) : withNullsLast.ThenBy(item => item.DeltaMagnitude)).ToArray();
        }
        else
            MatchResults = (SortDescending ? filtered.OrderByDescending(item => item.DimensionType).ThenByDescending(item => item.NominalValue) : filtered.OrderBy(item => item.DimensionType).ThenBy(item => item.NominalValue)).ToArray();
        Notify(nameof(MatchResults));
    }
    private void RaiseCommands() { ((RelayCommand)SelectPairCommand).Raise(); ((RelayCommand)MatchDimensionsCommand).Raise(); ((RelayCommand)RefreshMatchesCommand).Raise(); ((RelayCommand)UpdateToleranceCommand).Raise(); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void ClearResults(string message) { _allMatchResults = Array.Empty<DimensionMatchItemViewModel>(); MatchResults = Array.Empty<DimensionMatchItemViewModel>(); MatchingStatus = message; Notify(nameof(MatchResults)); Notify(nameof(MatchingStatus)); }
    private void SetEffectiveTolerance(double value, bool updateInput = false) { _effectiveToleranceThresholdMm = value; if (updateInput) { ToleranceThresholdInput = FormatTolerance(value); Notify(nameof(ToleranceThresholdInput)); } Notify(nameof(ToleranceSummary)); }
    private static string FormatTolerance(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
