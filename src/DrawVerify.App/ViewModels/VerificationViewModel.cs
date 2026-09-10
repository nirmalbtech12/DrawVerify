using DrawVerify.Core.Authorization;
using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Core.Configuration;
using System.Globalization;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.App.ViewModels;

public sealed class VerificationViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly DimensionMatchingService? _matching;
    private readonly ICentralizedLogger? _logger;
    private readonly double _configuredToleranceThresholdMm;
    private double? _effectiveToleranceThresholdMm;
    public VerificationViewModel(ISessionAuthenticationService session, RoleAuthorization authorization, DimensionMatchingService? matching = null, ICentralizedLogger? logger = null, DimensionMatchingConfiguration? configuration = null)
    {
        _session = session; _authorization = authorization; _matching = matching; _logger = logger; StepPreview = new StepPreviewViewModel(logger); _configuredToleranceThresholdMm = configuration?.ToleranceThresholdMm ?? 0.1; ToleranceThresholdInput = FormatTolerance(_configuredToleranceThresholdMm);
        SelectPairCommand = new RelayCommand(async _ => await SelectPairAsync(), _ => CanMatchDimensions && !IsBusy);
        MatchDimensionsCommand = new RelayCommand(async _ => await MatchSelectedDimensionsAsync(), _ => CanMatchDimensions && SelectedSessionId is not null && !IsBusy);
        RefreshMatchesCommand = new RelayCommand(async _ => await RefreshSelectedMatchesAsync(), _ => CanViewDimensionMatches && SelectedSessionId is not null && !IsBusy);
        UpdateToleranceCommand = new RelayCommand(async _ => await UpdateToleranceAsync(), _ => CanManageTolerance && SelectedSessionId is not null && !IsBusy);
        ApplyFilterCommand = new RelayCommand(_ => ApplyResultView()); SortResultsCommand = new RelayCommand(_ => ApplyResultView());
        AddMissingDimensionCommentCommand = new RelayCommand(async _ => await AddMissingDimensionCommentAsync(), _ => CanCommentMissingDimensions && SelectedSessionId is not null && SelectedUnmatchedDimension is not null && !IsBusy);
        ResolveMissingDimensionCommand = new RelayCommand(async value => await ResolveSelectedMissingDimensionAsync(value), _ => CanResolveMissingDimensions && SelectedSessionId is not null && SelectedUnmatchedDimension is not null && !IsBusy);
        AddExtraDimensionCommentCommand = new RelayCommand(async _ => await AddExtraDimensionCommentAsync(), _ => CanCommentExtraDimensions && SelectedSessionId is not null && SelectedExtraDimension is not null && !IsBusy);
        FlagExtraDimensionCommand = new RelayCommand(async value => await FlagSelectedExtraDimensionAsync(value), _ => CanFlagExtraDimensions && SelectedSessionId is not null && SelectedExtraDimension is not null && !IsBusy);
        AddGdtCommentCommand = new RelayCommand(async _ => await AddGdtCommentAsync(), _ => CanReview && SelectedSessionId is not null && SelectedGdtResult is not null && !IsBusy);
        FlagGdtEngineeringReviewCommand = new RelayCommand(async value => await FlagGdtEngineeringReviewAsync(value), _ => CanReview && SelectedSessionId is not null && SelectedGdtResult is not null && !IsBusy);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanReview => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ReviewVerification);
    public bool CanEnterReview => CanReview;
    public string DenialMessage => "The Checker or Approver role is required to review verification results.";
    public bool TryEnterReview(out string message) { if (CanReview) { message = string.Empty; return true; } message = DenialMessage; return false; }
    public bool CanMatchDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.MatchDimensionsToGeometry);
    public bool CanViewDimensionMatches => _matching is not null && (_authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewDimensionMatchResults) || _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewMissingDimensions) || _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewExtraDimensions));
    public bool CanManageTolerance => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageToleranceComparison);
    public bool CanCommentMissingDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.CommentOnMissingDimensions);
    public bool CanResolveMissingDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ResolveUnmatched);
    public bool CanCommentExtraDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.CommentOnExtraDimensions);
    public bool CanFlagExtraDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.FlagExtraDimensions);
    public string ToleranceThresholdInput { get; set; } = "0.1";
    public string StepDocumentId { get; set; } = string.Empty;
    public string DxfDocumentId { get; set; } = string.Empty;
    public Guid? SelectedSessionId { get; private set; }
    public bool IsBusy { get; private set; }
    public string StatusFilter { get; set; } = "All";
    public bool SortDescending { get; set; }
    public string SortMode { get; set; } = "Default";
    public IReadOnlyList<string> SortModes { get; } = new[] { "Default", "Status", "Delta magnitude", "Layer", "View", "X", "Y" };
    public string LayerFilter { get; set; } = string.Empty;
    public string ViewFilter { get; set; } = string.Empty;
    public string BlockFilter { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string UnmatchedDimensionsTitle => "Unmatched Dimensions";
    public IReadOnlyList<string> StatusFilters { get; } = new[] { "All", "Matched", "Mismatched", "Unmatched", "Manual review", "Extra Dimension" };
    public IReadOnlyList<DimensionStatusSummary> StatusSummaries { get; private set; } = Array.Empty<DimensionStatusSummary>();
    public ComparisonSummary ComparisonSummary { get; private set; } = new();
    public int TotalDimensions => ComparisonSummary.TotalDimensions;
    public int MatchedDimensions => ComparisonSummary.MatchedDimensions;
    public int MismatchedDimensions => ComparisonSummary.MismatchedDimensions;
    public int UnmatchedDimensions => ComparisonSummary.UnmatchedDimensions;
    public int MissingDimensions => ComparisonSummary.MissingDimensions;
    public int ExtraDimensions => ComparisonSummary.ExtraDimensions;
    public int NotVerifiedGdtControls => ComparisonSummary.NotVerifiedGdtControls;
    public IReadOnlyList<GdtResultItemViewModel> NotVerifiedGdtResults { get; private set; } = Array.Empty<GdtResultItemViewModel>();
    private GdtResultItemViewModel? _selectedGdtResult;
    public GdtResultItemViewModel? SelectedGdtResult
    {
        get => _selectedGdtResult;
        set
        {
            _selectedGdtResult = value;
            if (value is not null) NavigateToGdtSource(value.Result);
            Notify();
            RaiseCommands();
        }
    }
    public string GdtReviewComment { get; set; } = string.Empty;
    public string NotVerifiedGdtTitle => "Not Verified GD&T Controls";
    public double? MatchPercentage => ComparisonSummary.MatchPercentage;
    public string MatchPercentageText => MatchPercentage is double percentage ? $"{percentage.ToString("0.##", CultureInfo.InvariantCulture)}% Matched" : "N/A (no dimensions)";
    public string MatchRatio => TotalDimensions == 0 ? "0/0 Matched" : $"{MatchedDimensions}/{TotalDimensions} Matched";
    public IReadOnlyList<DimensionTypeSummary> DimensionTypeSummaries => ComparisonSummary.DimensionTypes;
    public ICommand SelectPairCommand { get; }
    public ICommand MatchDimensionsCommand { get; }
    public ICommand RefreshMatchesCommand { get; }
    public ICommand UpdateToleranceCommand { get; }
    public ICommand ApplyFilterCommand { get; }
    public ICommand SortResultsCommand { get; }
    public ICommand AddMissingDimensionCommentCommand { get; }
    public ICommand ResolveMissingDimensionCommand { get; }
    public ICommand AddExtraDimensionCommentCommand { get; }
    public ICommand FlagExtraDimensionCommand { get; }
    public ICommand AddGdtCommentCommand { get; }
    public ICommand FlagGdtEngineeringReviewCommand { get; }
    public string MatchingStatus { get; private set; } = "No matching session selected.";
    public string MissingDimensionComment { get; set; } = string.Empty;
    public string ExtraDimensionComment { get; set; } = string.Empty;
    public string ToleranceSummary => $"Tolerance: ±{FormatTolerance(_effectiveToleranceThresholdMm ?? _configuredToleranceThresholdMm)} mm (configured/session; explicit dimension tolerances override)";
    public IReadOnlyList<DimensionMatchItemViewModel> MatchResults { get; private set; } = Array.Empty<DimensionMatchItemViewModel>();
    public IReadOnlyList<DimensionMatchItemViewModel> UnmatchedResults => _allMatchResults.Where(item => item.IsUnmatched).ToArray();
    public IReadOnlyList<DimensionMatchItemViewModel> ExtraDimensionResults => _allMatchResults.Where(item => item.IsExtra).ToArray();
    public string ExtraDimensionsTitle => "Extra Dimensions (in 3D but not in 2D)";
    public string ExtraDimensionsEmptyText => ExtraDimensionResults.Count == 0 ? "No extra dimensions identified." : string.Empty;
    public string MissingDimensionDetailsLabel => "Missing from 3D (STEP)";
    public int MissingDimensionLocationTotal => _allMatchResults.Count(item => item.IsUnmatched);
    public IReadOnlyList<LocationBreakdown> MissingDimensionsByLayer => BuildBreakdown(item => item.Match.DxfLayer);
    public IReadOnlyList<LocationBreakdown> MissingDimensionsByView => BuildBreakdown(item => item.Match.ViewName);
    public DxfPreviewViewModel DxfPreview { get; } = new();
    public StepPreviewViewModel StepPreview { get; }
    private DimensionMatchItemViewModel? _selectedExtraDimension;
    public DimensionMatchItemViewModel? SelectedExtraDimension
    {
        get => _selectedExtraDimension;
        set
        {
            _selectedExtraDimension = value;
            if (value is not null && !StepPreview.HighlightAndFit(value.Match.StepGeometryReference, value.Match.LocationX, value.Match.LocationY))
                MatchingStatus = "STEP preview unavailable for the selected extra dimension.";
            Notify(); Notify(nameof(StepPreview)); Notify(nameof(MatchingStatus)); RaiseCommands();
        }
    }
    private DimensionMatchItemViewModel? _selectedUnmatchedDimension;
    public DimensionMatchItemViewModel? SelectedUnmatchedDimension
    {
        get => _selectedUnmatchedDimension;
        set
        {
            _selectedUnmatchedDimension = value;
            if (value is not null)
                DxfPreview.SelectDimension(value.Match.DxfDimensionReference, value.Match.PreviewAnchor, value.Match.LocationX, value.Match.LocationY);
            Notify();
            Notify(nameof(DxfPreview));
            RaiseCommands();
        }
    }
    public async Task AddMissingDimensionCommentAsync()
    {
        if (SelectedSessionId is not Guid sessionId || SelectedUnmatchedDimension is null) return;
        try
        {
            IsBusy = true; Notify(nameof(IsBusy));
            await _matching!.AddCommentAsync(sessionId, SelectedUnmatchedDimension.Match.DimensionMatchId, MissingDimensionComment);
            await RefreshMatchesAsync(sessionId);
            MissingDimensionComment = string.Empty; Notify(nameof(MissingDimensionComment));
            MatchingStatus = "Missing dimension comment saved."; Notify(nameof(MatchingStatus));
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Missing dimension comment", $"Unable to save dimensionMatchId={SelectedUnmatchedDimension.Match.DimensionMatchId}.");
            MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to save comment: {exception.Message}"; Notify(nameof(MatchingStatus));
        }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    public async Task AddExtraDimensionCommentAsync()
    {
        if (SelectedSessionId is not Guid sessionId || SelectedExtraDimension is null) return;
        var matchId = SelectedExtraDimension.Match.DimensionMatchId;
        try
        {
            IsBusy = true; Notify(nameof(IsBusy));
            await _matching!.AddExtraCommentAsync(sessionId, matchId, ExtraDimensionComment);
            await RefreshMatchesAsync(sessionId);
            ExtraDimensionComment = string.Empty; Notify(nameof(ExtraDimensionComment));
            MatchingStatus = "Extra dimension comment saved."; Notify(nameof(MatchingStatus));
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Extra dimension comment", $"Unable to save dimensionMatchId={matchId}.");
            MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to save extra dimension comment: {exception.Message}"; Notify(nameof(MatchingStatus));
        }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    private void NavigateToGdtSource(GdtResult result)
    {
        var isDxf = result.SourceDocument.Contains("DXF", StringComparison.OrdinalIgnoreCase) || result.SourceDocument.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);
        if (isDxf)
        {
            var reference = result.EntityHandle;
            if (!int.TryParse(reference, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var entityReference))
                int.TryParse(reference, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out entityReference);
            if (entityReference == 0)
            {
                var match = System.Text.RegularExpressions.Regex.Match(result.SourceLocation ?? string.Empty, @"(?:entity|ordinal)[:=](\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out entityReference);
            }
            if (!DxfPreview.SelectDimension(entityReference, result.SourceLocation))
                MatchingStatus = "DXF preview unavailable for the selected GD&T control.";
            else
                MatchingStatus = "DXF source location highlighted for the selected GD&T control.";
        }
        else if (!StepPreview.HighlightAndFit(result.EntityHandle ?? result.SourceLocation, null, null))
            MatchingStatus = "STEP preview unavailable for the selected GD&T control.";
        Notify(nameof(MatchingStatus));
        Notify(nameof(DxfPreview));
        Notify(nameof(StepPreview));
    }
    private async Task AddGdtCommentAsync()
    {
        if (SelectedSessionId is not Guid sessionId || SelectedGdtResult is null) return;
        try { IsBusy = true; Notify(nameof(IsBusy)); await _matching!.UpdateGdtReviewAsync(sessionId, SelectedGdtResult.Result.GdtResultId, GdtReviewComment, SelectedGdtResult.RequiresEngineeringReview); await RefreshMatchesAsync(sessionId); GdtReviewComment = string.Empty; Notify(nameof(GdtReviewComment)); MatchingStatus = "GD&T review comment saved."; Notify(nameof(MatchingStatus)); }
        catch (Exception exception) { _logger?.Log(exception, "GD&T review comment", $"Unable to save gdtResultId={SelectedGdtResult.Result.GdtResultId}."); MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to save GD&T review: {exception.Message}"; Notify(nameof(MatchingStatus)); }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    private async Task FlagGdtEngineeringReviewAsync(object? value)
    {
        if (SelectedSessionId is not Guid sessionId || SelectedGdtResult is null || !bool.TryParse(value?.ToString(), out var required)) return;
        try { IsBusy = true; Notify(nameof(IsBusy)); await _matching!.UpdateGdtReviewAsync(sessionId, SelectedGdtResult.Result.GdtResultId, SelectedGdtResult.Comment, required); await RefreshMatchesAsync(sessionId); MatchingStatus = required ? "GD&T engineering review requested." : "GD&T engineering review cleared."; Notify(nameof(MatchingStatus)); }
        catch (Exception exception) { _logger?.Log(exception, "GD&T engineering review", $"Unable to update gdtResultId={SelectedGdtResult.Result.GdtResultId}."); MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to update GD&T disposition: {exception.Message}"; Notify(nameof(MatchingStatus)); }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    private async Task FlagSelectedExtraDimensionAsync(object? value)
    {
        if (SelectedSessionId is not Guid sessionId || SelectedExtraDimension is null || !bool.TryParse(value?.ToString(), out var requiresUpdate)) return;
        var matchId = SelectedExtraDimension.Match.DimensionMatchId;
        try
        {
            IsBusy = true; Notify(nameof(IsBusy));
            await _matching!.FlagExtraForDesignUpdateAsync(sessionId, matchId, requiresUpdate);
            await RefreshMatchesAsync(sessionId);
            MatchingStatus = requiresUpdate ? "Extra dimension flagged for design update." : "Extra dimension design-update flag cleared."; Notify(nameof(MatchingStatus));
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Extra dimension design update flag", $"Unable to update dimensionMatchId={matchId}.");
            MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to update design flag: {exception.Message}"; Notify(nameof(MatchingStatus));
        }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    private async Task ResolveSelectedMissingDimensionAsync(object? value)
    {
        if (SelectedSessionId is not Guid sessionId || SelectedUnmatchedDimension is null || !Enum.TryParse<DimensionResolutionStatus>(value?.ToString(), true, out var status)) return;
        try
        {
            IsBusy = true; Notify(nameof(IsBusy));
            await _matching!.ResolveMissingDimensionAsync(sessionId, SelectedUnmatchedDimension.Match.DimensionMatchId, status);
            await RefreshMatchesAsync(sessionId);
            MatchingStatus = "Missing dimension resolution saved."; Notify(nameof(MatchingStatus));
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Missing dimension resolution", $"Unable to save dimensionMatchId={SelectedUnmatchedDimension.Match.DimensionMatchId}.");
            MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to save resolution: {exception.Message}"; Notify(nameof(MatchingStatus));
        }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    private IReadOnlyList<DimensionMatchItemViewModel> _allMatchResults = Array.Empty<DimensionMatchItemViewModel>();
    public async Task<bool> MatchDimensionsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!CanMatchDimensions) { ClearResults("Access denied."); return false; }
        try { SelectedSessionId = sessionId; var session = await _matching!.MatchDimensionsToGeometryAsync(sessionId, cancellationToken); SetEffectiveTolerance(session.ToleranceThresholdMm ?? _configuredToleranceThresholdMm, updateInput: true); MatchingStatus = session.Status.ToString(); await RefreshMatchesAsync(sessionId, cancellationToken); Notify(nameof(SelectedSessionId)); return true; }
        catch (UnauthorizedAccessException) { ClearResults("Access denied."); return false; }
        catch (Exception exception) { _logger?.Log(exception, "Dimension matching", $"Unable to match verificationSessionId={sessionId}."); ClearResults(exception.Message); return false; }
    }
    public async Task RefreshMatchesAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!CanViewDimensionMatches) { ClearResults("Access denied."); return; }
        try
        {
            if (_authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewDimensionMatchResults))
                SetEffectiveTolerance(await _matching!.GetEffectiveToleranceThresholdAsync(sessionId, cancellationToken));
            var canViewAll = _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewDimensionMatchResults);
            var canViewMissing = _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewMissingDimensions);
            var canViewExtra = _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewExtraDimensions);
            IReadOnlyList<DimensionMatch> matches;
            if (canViewAll)
                matches = await _matching!.GetResultsAsync(sessionId, cancellationToken: cancellationToken);
            else
            {
                var authorizedRows = new List<DimensionMatch>();
                if (canViewMissing)
                    authorizedRows.AddRange(await _matching!.GetMissingResultsAsync(sessionId, cancellationToken));
                if (canViewExtra)
                    authorizedRows.AddRange(await _matching!.GetExtraResultsAsync(sessionId, cancellationToken));
                matches = authorizedRows;
            }
            if (canViewExtra && matches.Any(match => match.Status is DimensionMatchStatus.ExtraDimension or DimensionMatchStatus.MissingDimension))
            {
                try
                {
                    var storedPath = await _matching!.GetStepPreviewPathAsync(sessionId, cancellationToken);
                    var previewPath = Path.IsPathRooted(storedPath) ? storedPath : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, storedPath));
                    if (!StepPreview.LoadModel(previewPath))
                        MatchingStatus = "STEP preview unavailable: the model could not be loaded.";
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    _logger?.Log(exception, "STEP preview model load", $"Unable to prepare preview for verificationSessionId={sessionId}; result rows remain available.");
                    MatchingStatus = "STEP preview unavailable: the model could not be loaded.";
                    Notify(nameof(MatchingStatus));
                }
            }
            IReadOnlyList<GdtResult> gdtResults = Array.Empty<GdtResult>();
            try
            {
                gdtResults = await _matching!.GetGdtResultsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                _logger?.Log(exception, "Load GD&T verification results", $"Unable to load GD&T results for verificationSessionId={sessionId}; dimension rows remain available.");
                MatchingStatus = "GD&T results unavailable; dimension results remain available.";
                Notify(nameof(MatchingStatus));
            }
            NotVerifiedGdtResults = gdtResults.Where(result => result.Status == VerificationStatus.NotVerified).Select(result => new GdtResultItemViewModel(result)).ToArray();
            _allMatchResults = matches.Select(match => new DimensionMatchItemViewModel(match)).ToArray(); ApplyResultView(); Notify(nameof(NotVerifiedGdtResults)); Notify(nameof(NotVerifiedGdtControls));
        }
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
    private async Task UpdateToleranceAsync() { if (SelectedSessionId is not Guid id || !double.TryParse(ToleranceThresholdInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) || !double.IsFinite(tolerance) || tolerance < 0) { MatchingStatus = "Enter a non-negative tolerance in millimetres."; Notify(nameof(MatchingStatus)); return; } IsBusy = true; Notify(nameof(IsBusy)); try { var session = await _matching!.UpdateToleranceAndRecompareAsync(id, tolerance); SetEffectiveTolerance(session.ToleranceThresholdMm ?? tolerance, updateInput: true); await RefreshMatchesAsync(id); MatchingStatus = $"Tolerance comparison completed using ±{FormatTolerance(tolerance)} mm."; Notify(nameof(MatchingStatus)); } catch (Exception exception) { if (exception is not UnauthorizedAccessException) _logger?.Log(exception, "Tolerance re-comparison", $"Unable to recompare verificationSessionId={id}."); ClearResults(exception is UnauthorizedAccessException ? "Access denied." : exception.Message); } finally { IsBusy = false; RaiseCommands(); } }
    private void ApplyResultView()
    {
        UpdateStatusSummaries();
        var filtered = _allMatchResults;
        if (!string.Equals(StatusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = StatusFilter switch
            {
                "Matched" => filtered.Where(item => item.Match.Status == DimensionMatchStatus.Matched).ToArray(),
                "Mismatched" => filtered.Where(item => item.Match.Status == DimensionMatchStatus.Mismatched).ToArray(),
                "Unmatched" => filtered.Where(item => item.Match.Status == DimensionMatchStatus.Unmatched).ToArray(),
                _ => filtered.Where(item => string.Equals(item.StatusLabel, StatusFilter, StringComparison.OrdinalIgnoreCase)).ToArray()
            };
        }
        if (!string.IsNullOrWhiteSpace(SearchText)) filtered = filtered.Where(item => string.Join(" ", item.DimensionText, item.NominalValue, item.DimensionType, item.Reason, item.ReasonExplanation, item.Match.DxfDimensionReference).Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!string.IsNullOrWhiteSpace(LayerFilter)) filtered = filtered.Where(item => string.Equals(item.Match.DxfLayer, LayerFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!string.IsNullOrWhiteSpace(ViewFilter)) filtered = filtered.Where(item => string.Equals(item.Match.ViewName, ViewFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!string.IsNullOrWhiteSpace(BlockFilter)) filtered = filtered.Where(item => string.Equals(item.Match.BlockName, BlockFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (string.Equals(SortMode, "Status", StringComparison.OrdinalIgnoreCase))
        {
            var ordered = SortDescending
                ? filtered.OrderByDescending(item => StatusPriority(item.Match.Status))
                : filtered.OrderBy(item => StatusPriority(item.Match.Status));
            MatchResults = ordered.ToArray();
        }
        else if (string.Equals(SortMode, "Delta magnitude", StringComparison.OrdinalIgnoreCase))
        {
            var withNullsLast = filtered.OrderBy(item => item.DeltaMagnitude is null);
            MatchResults = (SortDescending ? withNullsLast.ThenByDescending(item => item.DeltaMagnitude) : withNullsLast.ThenBy(item => item.DeltaMagnitude)).ToArray();
        }
        else if (string.Equals(SortMode, "Layer", StringComparison.OrdinalIgnoreCase))
            MatchResults = (SortDescending ? filtered.OrderByDescending(item => item.Match.DxfLayer ?? string.Empty).ThenBy(item => item.Match.DxfDimensionReference) : filtered.OrderBy(item => item.Match.DxfLayer ?? string.Empty).ThenBy(item => item.Match.DxfDimensionReference)).ToArray();
        else if (string.Equals(SortMode, "View", StringComparison.OrdinalIgnoreCase))
            MatchResults = (SortDescending ? filtered.OrderByDescending(item => item.Match.ViewName ?? string.Empty).ThenBy(item => item.Match.DxfDimensionReference) : filtered.OrderBy(item => item.Match.ViewName ?? string.Empty).ThenBy(item => item.Match.DxfDimensionReference)).ToArray();
        else if (string.Equals(SortMode, "X", StringComparison.OrdinalIgnoreCase))
            MatchResults = (SortDescending ? filtered.OrderByDescending(item => item.Match.LocationX is null).ThenByDescending(item => item.Match.LocationX).ThenBy(item => item.Match.DxfDimensionReference) : filtered.OrderBy(item => item.Match.LocationX is null).ThenBy(item => item.Match.LocationX).ThenBy(item => item.Match.DxfDimensionReference)).ToArray();
        else if (string.Equals(SortMode, "Y", StringComparison.OrdinalIgnoreCase))
            MatchResults = (SortDescending ? filtered.OrderByDescending(item => item.Match.LocationY is null).ThenByDescending(item => item.Match.LocationY).ThenBy(item => item.Match.DxfDimensionReference) : filtered.OrderBy(item => item.Match.LocationY is null).ThenBy(item => item.Match.LocationY).ThenBy(item => item.Match.DxfDimensionReference)).ToArray();
        else
            MatchResults = (SortDescending ? filtered.OrderByDescending(item => item.DimensionType).ThenByDescending(item => item.NominalValue) : filtered.OrderBy(item => item.DimensionType).ThenBy(item => item.NominalValue)).ToArray();
        Notify(nameof(MatchResults));
        Notify(nameof(UnmatchedResults));
        Notify(nameof(ExtraDimensionResults));
        Notify(nameof(ExtraDimensionsEmptyText));
        Notify(nameof(MissingDimensionLocationTotal));
        Notify(nameof(MissingDimensionsByLayer));
        Notify(nameof(MissingDimensionsByView));
    }
    private void UpdateStatusSummaries()
    {
        ComparisonSummary = DrawVerify.Core.Models.ComparisonSummary.From(_allMatchResults.Select(item => item.Match), NotVerifiedGdtResults.Select(item => item.Result));
        StatusSummaries = Enum.GetValues<DimensionType>().Select(type =>
        {
            var matches = _allMatchResults.Where(item => item.Match.DimensionType == type).Select(item => item.Match);
            return new DimensionStatusSummary(type,
                matches.Count(match => match.Status == DimensionMatchStatus.Matched),
                matches.Count(match => match.Status == DimensionMatchStatus.Mismatched),
                matches.Count(match => match.Status == DimensionMatchStatus.Unmatched),
                matches.Count(match => match.Status is DimensionMatchStatus.ExtraDimension or DimensionMatchStatus.MissingDimension));
        }).ToArray();
        Notify(nameof(ComparisonSummary));
        Notify(nameof(TotalDimensions));
        Notify(nameof(MatchedDimensions));
        Notify(nameof(MismatchedDimensions));
        Notify(nameof(UnmatchedDimensions));
        Notify(nameof(MissingDimensions));
        Notify(nameof(ExtraDimensions));
        Notify(nameof(NotVerifiedGdtControls));
        Notify(nameof(MatchPercentage));
        Notify(nameof(MatchPercentageText));
        Notify(nameof(MatchRatio));
        Notify(nameof(DimensionTypeSummaries));
        Notify(nameof(StatusSummaries));
        Notify(nameof(MissingDimensionLocationTotal));
        Notify(nameof(MissingDimensionsByLayer));
        Notify(nameof(MissingDimensionsByView));
    }
    private IReadOnlyList<LocationBreakdown> BuildBreakdown(Func<DimensionMatchItemViewModel, string?> selector) => _allMatchResults.Where(item => item.IsUnmatched).Select(item => selector(item)).Where(value => !string.IsNullOrWhiteSpace(value)).GroupBy(value => value!, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).Select(group => new LocationBreakdown(group.Key, group.Count())).ToArray();
    private static int StatusPriority(DimensionMatchStatus status) => status switch
    {
        DimensionMatchStatus.Mismatched => 0,
        DimensionMatchStatus.Unmatched => 1,
        DimensionMatchStatus.Matched => 2,
        _ => 3
    };
    private void RaiseCommands() { ((RelayCommand)SelectPairCommand).Raise(); ((RelayCommand)MatchDimensionsCommand).Raise(); ((RelayCommand)RefreshMatchesCommand).Raise(); ((RelayCommand)UpdateToleranceCommand).Raise(); ((RelayCommand)AddMissingDimensionCommentCommand).Raise(); ((RelayCommand)ResolveMissingDimensionCommand).Raise(); ((RelayCommand)AddExtraDimensionCommentCommand).Raise(); ((RelayCommand)FlagExtraDimensionCommand).Raise(); ((RelayCommand)AddGdtCommentCommand).Raise(); ((RelayCommand)FlagGdtEngineeringReviewCommand).Raise(); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { StepPreview.Dispose(); GC.SuppressFinalize(this); }
    private void ClearResults(string message) { _allMatchResults = Array.Empty<DimensionMatchItemViewModel>(); MatchResults = Array.Empty<DimensionMatchItemViewModel>(); UpdateStatusSummaries(); MatchingStatus = message; Notify(nameof(MatchResults)); Notify(nameof(UnmatchedResults)); Notify(nameof(MatchingStatus)); }
    private void SetEffectiveTolerance(double value, bool updateInput = false) { _effectiveToleranceThresholdMm = value; if (updateInput) { ToleranceThresholdInput = FormatTolerance(value); Notify(nameof(ToleranceThresholdInput)); } Notify(nameof(ToleranceSummary)); }
    private static string FormatTolerance(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}

public sealed record DimensionStatusSummary(DimensionType DimensionType, int MatchedCount, int MismatchedCount, int UnmatchedCount, int ExtraCount = 0)
{
    public int TotalCount => MatchedCount + MismatchedCount + UnmatchedCount + ExtraCount;
}

public sealed record LocationBreakdown(string Location, int Count);
