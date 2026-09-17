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

public sealed class VerificationViewModel : TaskScreenViewModel, IDisposable
{
    public string? HighlightIdentifier { get; private set; }
    public string HighlightStatus => string.IsNullOrWhiteSpace(HighlightIdentifier) ? string.Empty : $"Highlighted review finding: {HighlightIdentifier}";
    public void SetHighlight(string? identifier) { HighlightIdentifier = identifier; Notify(nameof(HighlightIdentifier)); Notify(nameof(HighlightStatus)); ApplyHighlight(); }
    public async Task LoadReviewContextAsync(ReviewPackage package, string? highlightIdentifier, CancellationToken cancellationToken = default)
    {
        SetHighlight(highlightIdentifier);
        var persistedStamp = package.Submission.InternalApprovalStamp;
        StepPreview.SetInternalApprovalStamp(persistedStamp);
        DxfPreview.SetInternalApprovalStamp(persistedStamp);
        StepDocumentId = package.StepDocumentId.ToString();
        DxfDocumentId = package.DxfDocumentId.ToString();
        StepDocumentVersionId = package.StepDocumentVersionId.ToString();
        DxfDocumentVersionId = package.DxfDocumentVersionId.ToString();
        if (package.VerificationSessionId != Guid.Empty)
        {
            SelectedSessionId = package.VerificationSessionId;
            await RefreshMatchesAsync(package.VerificationSessionId, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await SelectPairAsync(package.StepDocumentId, package.DxfDocumentId, package.StepDocumentVersionId, package.DxfDocumentVersionId, cancellationToken).ConfigureAwait(true);
        }
        Notify(nameof(SelectedSessionId));
        ApplyHighlight();
    }
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly DimensionMatchingService? _matching;
    private readonly ICentralizedLogger? _logger;
    private readonly double _configuredToleranceThresholdMm;
    private double? _effectiveToleranceThresholdMm;
    private bool _trackUserChanges;
    private string _toleranceThresholdInput = "0.1";
    private string _statusFilter = "All";
    private bool _sortDescending;
    private string _sortMode = "Default";
    private string _layerFilter = string.Empty;
    private string _viewFilter = string.Empty;
    private string _blockFilter = string.Empty;
    private string _searchText = string.Empty;
    private string _gdtReviewComment = string.Empty;
    private string _missingDimensionComment = string.Empty;
    private string _extraDimensionComment = string.Empty;
    private string _gdtDisposition = string.Empty;
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
        AddGdtCommentCommand = new RelayCommand(async _ => await AddGdtCommentAsync(), _ => CanModifyGdtReview && SelectedSessionId is not null && SelectedGdtResult is not null && !IsBusy);
        FlagGdtEngineeringReviewCommand = new RelayCommand(async value => await FlagGdtEngineeringReviewAsync(value), _ => CanModifyGdtReview && SelectedSessionId is not null && SelectedGdtResult is not null && !IsBusy);
        SetGdtDispositionCommand = new RelayCommand(async value => await SetGdtDispositionAsync(value), _ => CanModifyGdtReview && SelectedSessionId is not null && SelectedGdtResult is not null && !IsBusy);
        _trackUserChanges = true;
    }
    public bool CanReview => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ReviewVerification);
    public bool CanModifyGdtReview => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ModifyGdtCannotValidate);
    public bool CanEnterReview => CanReview;
    public string DenialMessage => "The Checker or Approver role is required to review verification results.";
    public bool TryEnterReview(out string message) { if (CanReview) { message = string.Empty; return true; } message = DenialMessage; return false; }
    public bool CanMatchDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.StartVerification);
    public bool CanViewDimensionMatches => _matching is not null && (_authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewDimensionMatchResults) || _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewMissingDimensions) || _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewExtraDimensions) || _authorization.IsAllowed(_session.CurrentUser, RolePermission.ViewGdtCannotValidate));
    public bool CanManageTolerance => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageToleranceComparison);
    public bool CanCommentMissingDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.CommentOnMissingDimensions);
    public bool CanResolveMissingDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.ResolveUnmatched);
    public bool CanCommentExtraDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.CommentOnExtraDimensions);
    public bool CanFlagExtraDimensions => _matching is not null && _authorization.IsAllowed(_session.CurrentUser, RolePermission.FlagExtraDimensions);
    public string ToleranceThresholdInput { get => _toleranceThresholdInput; set => SetUserString(ref _toleranceThresholdInput, value); }
    public string StepDocumentId { get => _stepDocumentId; set { if (_stepDocumentId == value) return; _stepDocumentId = value; MarkDirty(); Notify(); } }
    public string DxfDocumentId { get => _dxfDocumentId; set { if (_dxfDocumentId == value) return; _dxfDocumentId = value; MarkDirty(); Notify(); } }
    public string StepDocumentVersionId { get => _stepDocumentVersionId; set { if (_stepDocumentVersionId == value) return; _stepDocumentVersionId = value; MarkDirty(); Notify(); } }
    public string DxfDocumentVersionId { get => _dxfDocumentVersionId; set { if (_dxfDocumentVersionId == value) return; _dxfDocumentVersionId = value; MarkDirty(); Notify(); } }
    private string _stepDocumentId = string.Empty;
    private string _dxfDocumentId = string.Empty;
    private string _stepDocumentVersionId = string.Empty;
    private string _dxfDocumentVersionId = string.Empty;
    public Guid? SelectedSessionId { get; private set; }
    public bool IsBusy { get; private set; }
    public string StatusFilter { get => _statusFilter; set => SetUserString(ref _statusFilter, value); }
    public bool SortDescending { get => _sortDescending; set { if (_sortDescending == value) return; _sortDescending = value; MarkUserChange(); Notify(); } }
    public string SortMode { get => _sortMode; set => SetUserString(ref _sortMode, value); }
    public IReadOnlyList<string> SortModes { get; } = new[] { "Default", "Status", "Delta magnitude", "Layer", "View", "X", "Y" };
    public string LayerFilter { get => _layerFilter; set => SetUserString(ref _layerFilter, value); }
    public string ViewFilter { get => _viewFilter; set => SetUserString(ref _viewFilter, value); }
    public string BlockFilter { get => _blockFilter; set => SetUserString(ref _blockFilter, value); }
    public string SearchText { get => _searchText; set => SetUserString(ref _searchText, value); }
    public string UnmatchedDimensionsTitle => "Unmatched Dimensions";
    public IReadOnlyList<string> StatusFilters { get; } = new[] { "All", "Matched", "Mismatched", "Unmatched", "Manual review", "Cannot Validate", "Extra Dimension" };
    public IReadOnlyList<DimensionStatusSummary> StatusSummaries { get; private set; } = Array.Empty<DimensionStatusSummary>();
    public ComparisonSummary ComparisonSummary { get; private set; } = new();
    public int TotalDimensions => ComparisonSummary.TotalDimensions;
    public int MatchedDimensions => ComparisonSummary.MatchedDimensions;
    public int MismatchedDimensions => ComparisonSummary.MismatchedDimensions;
    public int UnmatchedDimensions => ComparisonSummary.UnmatchedDimensions;
    public int MissingDimensions => ComparisonSummary.MissingDimensions;
    public int ExtraDimensions => ComparisonSummary.ExtraDimensions;
    public int NotVerifiedGdtControls => ComparisonSummary.NotVerifiedGdtControls;
    public int CannotValidateGdtControls => ComparisonSummary.CannotValidateGdtControls;
    public IReadOnlyList<GdtResultItemViewModel> NotVerifiedGdtResults { get; private set; } = Array.Empty<GdtResultItemViewModel>();
    public IReadOnlyList<GdtResultItemViewModel> CannotValidateGdtResults { get; private set; } = Array.Empty<GdtResultItemViewModel>();
    public IReadOnlyList<GdtResultItemViewModel> GdtResults { get; private set; } = Array.Empty<GdtResultItemViewModel>();
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
    public string GdtReviewComment { get => _gdtReviewComment; set => SetUserString(ref _gdtReviewComment, value); }
    public string NotVerifiedGdtTitle => "GD&T Validation Results";
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
    public ICommand SetGdtDispositionCommand { get; }
    public string MatchingStatus { get; private set; } = "No matching session selected.";
    public string EmptyStateText => MatchResults.Count == 0 ? "No items found" : string.Empty;
    public string MissingDimensionComment { get => _missingDimensionComment; set => SetUserString(ref _missingDimensionComment, value); }
    public string ExtraDimensionComment { get => _extraDimensionComment; set => SetUserString(ref _extraDimensionComment, value); }
    public string GdtDisposition { get => _gdtDisposition; set => SetUserString(ref _gdtDisposition, value); }
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
            if (entityReference <= 0 || string.IsNullOrWhiteSpace(result.SourceLocation) || !DxfPreview.SelectDimension(entityReference, result.SourceLocation))
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
    private async Task SetGdtDispositionAsync(object? value)
    {
        if (SelectedSessionId is not Guid sessionId || SelectedGdtResult is null || value is null) return;
        var disposition = value.ToString();
        if (string.IsNullOrWhiteSpace(disposition)) return;
        try
        {
            IsBusy = true; Notify(nameof(IsBusy));
            await _matching!.UpdateGdtDispositionAsync(sessionId, SelectedGdtResult.Result.GdtResultId, disposition);
            await RefreshMatchesAsync(sessionId);
            MatchingStatus = $"GD&T disposition saved: {disposition}."; Notify(nameof(MatchingStatus));
        }
        catch (Exception exception) { _logger?.Log(exception, "GD&T disposition", $"Unable to update gdtResultId={SelectedGdtResult.Result.GdtResultId}."); MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : $"Unable to update GD&T disposition: {exception.Message}"; Notify(nameof(MatchingStatus)); }
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
            // Load the complete STEP model independently of result status. A fully
            // matched drawing still needs a usable 3D view; feature highlighting
            // is an optional navigation action on top of the loaded model.
            if (canViewExtra)
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
            GdtResults = gdtResults.Select(result => new GdtResultItemViewModel(result)).ToArray();
            NotVerifiedGdtResults = GdtResults.Where(result => result.Result.Status == VerificationStatus.NotVerified).ToArray();
            CannotValidateGdtResults = GdtResults.Where(result => result.Result.Status == VerificationStatus.CannotValidate).ToArray();
            _allMatchResults = matches.Select(match => new DimensionMatchItemViewModel(match)).ToArray(); ApplyResultView(); Notify(nameof(GdtResults)); Notify(nameof(NotVerifiedGdtResults)); Notify(nameof(CannotValidateGdtResults)); Notify(nameof(NotVerifiedGdtControls)); Notify(nameof(CannotValidateGdtControls));
            ApplyHighlight();
        }
        catch (UnauthorizedAccessException) { ClearResults("Access denied."); }
        catch (Exception exception) { _logger?.Log(exception, "Load dimension matching results", $"Unable to load verificationSessionId={sessionId}."); ClearResults("Unable to load matching results."); }
    }
    private DimensionMatchItemViewModel? _selectedMatch;
    public DimensionMatchItemViewModel? SelectedMatch
    {
        get => _selectedMatch;
        set { _selectedMatch = value; Notify(); RaiseCommands(); }
    }
    private void ApplyHighlight()
    {
        if (string.IsNullOrWhiteSpace(HighlightIdentifier)) return;
        var gdt = GdtResults.FirstOrDefault(item => string.Equals(item.Result.GdtResultId.ToString(), HighlightIdentifier, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Result.Identifier, HighlightIdentifier, StringComparison.OrdinalIgnoreCase));
        if (gdt is not null) { SelectedGdtResult = gdt; MatchingStatus = $"Highlighted GD&T control {HighlightIdentifier}."; Notify(nameof(MatchingStatus)); return; }
        var dimension = _allMatchResults.FirstOrDefault(item => string.Equals(item.Match.DimensionMatchId.ToString(), HighlightIdentifier, StringComparison.OrdinalIgnoreCase) || item.Match.DxfDimensionReference.ToString(CultureInfo.InvariantCulture) == HighlightIdentifier);
        if (dimension is not null) { SelectedMatch = dimension; if (dimension.IsUnmatched) SelectedUnmatchedDimension = dimension; else if (dimension.IsExtra) SelectedExtraDimension = dimension; MatchingStatus = $"Highlighted dimension {HighlightIdentifier}."; Notify(nameof(MatchingStatus)); }
    }
    private async Task SelectPairAsync()
    {
        if (!Guid.TryParse(StepDocumentId, out var stepId) || !Guid.TryParse(DxfDocumentId, out var dxfId) || !Guid.TryParse(StepDocumentVersionId, out var stepVersionId) || !Guid.TryParse(DxfDocumentVersionId, out var dxfVersionId)) { MatchingStatus = "Enter valid STEP, DXF, and selected version IDs."; Notify(nameof(MatchingStatus)); return; }
        try { IsBusy = true; Notify(nameof(IsBusy)); var session = await _matching!.GetOrCreateSessionAsync(stepId, dxfId, stepVersionId, dxfVersionId); SelectedSessionId = session.VerificationSessionId; MatchingStatus = $"Selected session {session.VerificationSessionId}"; await RefreshMatchesAsync(session.VerificationSessionId); Notify(nameof(SelectedSessionId)); }
        catch (Exception exception) { MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : exception.Message; MatchResults = Array.Empty<DimensionMatchItemViewModel>(); Notify(nameof(MatchingStatus)); Notify(nameof(MatchResults)); }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    public async Task<bool> SelectPairAsync(Guid stepDocumentId, Guid dxfDocumentId, Guid stepDocumentVersionId, Guid dxfDocumentVersionId, CancellationToken cancellationToken = default)
    {
        if (stepDocumentId == Guid.Empty || dxfDocumentId == Guid.Empty)
        {
            MatchingStatus = "Both a valid STEP and DXF document are required.";
            Notify(nameof(MatchingStatus));
            return false;
        }
        StepDocumentId = stepDocumentId.ToString();
        DxfDocumentId = dxfDocumentId.ToString();
        StepDocumentVersionId = stepDocumentVersionId.ToString();
        DxfDocumentVersionId = dxfDocumentVersionId.ToString();
        try
        {
            IsBusy = true; Notify(nameof(IsBusy));
            var session = await _matching!.GetOrCreateSessionAsync(stepDocumentId, dxfDocumentId, stepDocumentVersionId, dxfDocumentVersionId, cancellationToken).ConfigureAwait(true);
            SelectedSessionId = session.VerificationSessionId;
            MatchingStatus = $"Selected session {session.VerificationSessionId}.";
            await RefreshMatchesAsync(session.VerificationSessionId, cancellationToken).ConfigureAwait(true);
            Notify(nameof(SelectedSessionId));
            return true;
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Verification session selection", $"Unable to select stepDocumentId={stepDocumentId}; dxfDocumentId={dxfDocumentId}.");
            MatchingStatus = exception is UnauthorizedAccessException ? "Access denied." : exception.Message;
            Notify(nameof(MatchingStatus));
            return false;
        }
        finally { IsBusy = false; Notify(nameof(IsBusy)); RaiseCommands(); }
    }
    public void ResetWorkflowState()
    {
        SelectedSessionId = null;
        _stepDocumentId = string.Empty;
        _dxfDocumentId = string.Empty;
        _allMatchResults = Array.Empty<DimensionMatchItemViewModel>();
        MatchResults = Array.Empty<DimensionMatchItemViewModel>();
        _selectedUnmatchedDimension = null;
        _selectedExtraDimension = null;
        ClearResults("No matching session selected.");
        MatchingStatus = "No matching session selected.";
        Notify(nameof(StepDocumentId)); Notify(nameof(DxfDocumentId)); Notify(nameof(SelectedSessionId));
        Notify(nameof(SelectedUnmatchedDimension)); Notify(nameof(SelectedExtraDimension)); Notify(nameof(MatchingStatus));
        RaiseCommands();
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
        ComparisonSummary = DrawVerify.Core.Models.ComparisonSummary.From(_allMatchResults.Select(item => item.Match), GdtResults.Select(item => item.Result));
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
        Notify(nameof(CannotValidateGdtControls));
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
    private void RaiseCommands() { ((RelayCommand)SelectPairCommand).Raise(); ((RelayCommand)MatchDimensionsCommand).Raise(); ((RelayCommand)RefreshMatchesCommand).Raise(); ((RelayCommand)UpdateToleranceCommand).Raise(); ((RelayCommand)AddMissingDimensionCommentCommand).Raise(); ((RelayCommand)ResolveMissingDimensionCommand).Raise(); ((RelayCommand)AddExtraDimensionCommentCommand).Raise(); ((RelayCommand)FlagExtraDimensionCommand).Raise(); ((RelayCommand)AddGdtCommentCommand).Raise(); ((RelayCommand)FlagGdtEngineeringReviewCommand).Raise(); ((RelayCommand)SetGdtDispositionCommand).Raise(); }
    private void SetUserString(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        MarkUserChange();
        Notify(name);
    }
    private void MarkUserChange()
    {
        if (_trackUserChanges) MarkDirty();
    }
    private void Notify([CallerMemberName] string? name = null) => RaisePropertyChanged(name);
    public void Dispose() { StepPreview.Dispose(); GC.SuppressFinalize(this); }
    private void ClearResults(string message)
    {
        _allMatchResults = Array.Empty<DimensionMatchItemViewModel>();
        MatchResults = Array.Empty<DimensionMatchItemViewModel>();
        GdtResults = Array.Empty<GdtResultItemViewModel>();
        NotVerifiedGdtResults = Array.Empty<GdtResultItemViewModel>();
        CannotValidateGdtResults = Array.Empty<GdtResultItemViewModel>();
        SelectedGdtResult = null;
        UpdateStatusSummaries();
        MatchingStatus = message;
        Notify(nameof(MatchResults)); Notify(nameof(UnmatchedResults)); Notify(nameof(GdtResults));
        Notify(nameof(NotVerifiedGdtResults)); Notify(nameof(CannotValidateGdtResults)); Notify(nameof(SelectedGdtResult));
        Notify(nameof(NotVerifiedGdtControls)); Notify(nameof(CannotValidateGdtControls)); Notify(nameof(ComparisonSummary)); Notify(nameof(MatchingStatus));
    }
    private void SetEffectiveTolerance(double value, bool updateInput = false) { _effectiveToleranceThresholdMm = value; if (updateInput) { ToleranceThresholdInput = FormatTolerance(value); Notify(nameof(ToleranceThresholdInput)); } Notify(nameof(ToleranceSummary)); }
    private static string FormatTolerance(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}

public sealed record DimensionStatusSummary(DimensionType DimensionType, int MatchedCount, int MismatchedCount, int UnmatchedCount, int ExtraCount = 0)
{
    public int TotalCount => MatchedCount + MismatchedCount + UnmatchedCount + ExtraCount;
}

public sealed record LocationBreakdown(string Location, int Count);
