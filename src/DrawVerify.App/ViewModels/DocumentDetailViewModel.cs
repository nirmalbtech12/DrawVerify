using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Authorization;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreUploadStatus = DrawVerify.Core.Enums.UploadStatus;

namespace DrawVerify.App.ViewModels;

public sealed class DocumentDetailViewModel : INotifyPropertyChanged
{
    private readonly DxfDimensionExtractionService? _dimensionService;
    private Document? _document;
    private IReadOnlyList<DimensionData> _dimensions = Array.Empty<DimensionData>();
    private string _message = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private readonly IWorkflowService? _workflowService;
    private readonly SubmissionRequest? _submissionRequest;
    private readonly ISessionAuthenticationService? _authentication;
    private readonly RoleAuthorization _authorization = new();
    private readonly DocumentService? _documentService;
    private readonly ICentralizedLogger? _logger;
    private IReadOnlyList<PriorVersionReview> _priorVersions = Array.Empty<PriorVersionReview>();
    private PriorVersionReview? _selectedPriorVersion;
    private VersionComparison? _versionComparison;
    private WorkflowStatus? _priorStatusFilter;
    private DateTime? _priorFromUtc;
    private DateTime? _priorToUtc;
    private string? _priorMakerFilter;
    private FingerprintVerificationResult? _fingerprintVerification;
    private PriorVersionReview? _submittedRevisionSnapshot;
    private string? _submittedSnapshotError;
    private long _snapshotLoadGeneration;
    private readonly ViewerAccessViewModel? _viewerAccess;

    public DocumentDetailViewModel()
    {
        ExtractDimensionsCommand = new RelayCommand(async _ => await ExtractDimensionsAsync().ConfigureAwait(true), _ => CanExtractDimensions && !IsBusy);
        LoadDimensionsCommand = new RelayCommand(async _ => await LoadDimensionsAsync().ConfigureAwait(true), _ => CanViewDimensions && !IsBusy);
        LoadPriorVersionsCommand = new RelayCommand(async _ => await LoadPriorVersionsAsync().ConfigureAwait(true), _ => CanViewPriorVersions && !IsBusy);
        SelectPriorVersionCommand = new RelayCommand(async value => await SelectPriorVersionAsync(value).ConfigureAwait(true), value => CanViewPriorVersions && value is PriorVersionReview && !IsBusy);
        ComparePriorVersionCommand = new RelayCommand(async _ => await ComparePriorVersionAsync().ConfigureAwait(true), _ => CanViewPriorVersions && SelectedPriorVersion is not null && !IsBusy);
        ViewCurrentVersionCommand = new RelayCommand(_ => ViewCurrentVersion(), _ => SelectedPriorVersion is not null);
        OpenHistoricalStepCommand = new RelayCommand(async _ => await OpenHistoricalFileAsync(false).ConfigureAwait(true), _ => CanOpenHistoricalFiles && SelectedPriorVersion is not null && !IsBusy);
        OpenHistoricalDxfCommand = new RelayCommand(async _ => await OpenHistoricalFileAsync(true).ConfigureAwait(true), _ => CanOpenHistoricalFiles && SelectedPriorVersion is not null && !IsBusy);
        VerifyFingerprintCommand = new RelayCommand(async _ => await VerifyFingerprintAsync().ConfigureAwait(true), _ => CanVerifyFingerprint && !IsBusy);
        FlagFingerprintMismatchCommand = new RelayCommand(async _ => await FlagFingerprintMismatchAsync().ConfigureAwait(true), _ => CanEscalateFingerprintMismatch && !IsBusy);
        RejectFingerprintMismatchCommand = new RelayCommand(async _ => await DecideFingerprintMismatchAsync(ReviewDecisionType.Reject).ConfigureAwait(true), _ => CanEscalateFingerprintMismatch && !IsBusy);
        ReturnFingerprintMismatchCommand = new RelayCommand(async _ => await DecideFingerprintMismatchAsync(ReviewDecisionType.Return).ConfigureAwait(true), _ => CanEscalateFingerprintMismatch && !IsBusy);
    }

    public DocumentDetailViewModel(Document document) : this(document, null) { }

    public DocumentDetailViewModel(Document document, DxfDimensionExtractionService? dimensionService, IWorkflowService? workflowService = null, SubmissionRequest? submissionRequest = null, ISessionAuthenticationService? authentication = null, RoleAuthorization? authorization = null, DocumentService? documentService = null, ICentralizedLogger? logger = null, IViewerAccessService? viewerAccessService = null, ViewerAccessConfiguration? viewerAccessConfiguration = null) : this()
    {
        _dimensionService = dimensionService; _workflowService = workflowService; _submissionRequest = submissionRequest; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization(); _documentService = documentService; _logger = logger;
        if (viewerAccessService is not null && document.Version is not null) _viewerAccess = new ViewerAccessViewModel(viewerAccessService, document.DocumentId, document.Version.VersionId, viewerAccessConfiguration);
        SubmitForReviewCommand = new RelayCommand(_ => RaiseSubmissionRequested(), _ => CanSubmitForReview);
        Load(document);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Guid>? SubmitRequested;
    public event Action<SubmissionRequest>? SubmissionRequested;
    public ICommand SubmitForReviewCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
    public bool CanSubmitForReview => _workflowService is not null && _document is not null && (_authentication is null || (_authentication.IsAuthenticated && _authorization.IsAllowed(_authentication.CurrentUser, RolePermission.SubmitForReview)));
    public Document? Document => _document;
    public ViewerAccessViewModel? ViewerAccess => _viewerAccess;
    public bool CanManageViewerAccess => _viewerAccess?.CanManageAccess == true;
    public string OriginalFileName => _document?.OriginalFileName ?? string.Empty;
    public long FileSizeBytes => _document?.FileSizeBytes ?? 0;
    public DateTime UploadTimestampUtc => _document?.UploadTimestampUtc ?? default;
    public CoreDocumentType DocumentType => _document?.DocumentType ?? CoreDocumentType.Unknown;
    public CoreUploadStatus UploadStatus => _document?.UploadStatus ?? CoreUploadStatus.Pending;
    public VerificationStatus VerificationStatus => _document?.VerificationStatus ?? VerificationStatus.Unknown;
    public string? VerificationError => _document?.VerificationError;
    public string? Fingerprint => _document?.Version?.Fingerprint?.Value;
    public string FingerprintDisplay => Fingerprint ?? "No fingerprint is stored for this version.";
    public string UploaderUserId => _document?.UploaderUserId ?? string.Empty;
    public string UploaderDisplayName => _document?.UploaderDisplayName ?? string.Empty;
    public string UploaderDisplayNameSnapshot => UploaderDisplayName;
    public string VerificationStatusDisplay => VerificationStatus switch
    {
        VerificationStatus.PendingVerification => "Pending Verification",
        VerificationStatus.DimensionsExtracted => "Dimensions Extracted",
        VerificationStatus.Error => "Error",
        _ => VerificationStatus.ToString()
    };
    public DrawVerify.Core.Enums.GeometryStatus GeometryStatus => _document?.GeometryStatus ?? DrawVerify.Core.Enums.GeometryStatus.NotExtracted;
    public string? GeometryError => _document?.GeometryError;
    public int CurrentVersion => _document?.CurrentVersion ?? 0;

    public IReadOnlyList<DimensionData> Dimensions => _dimensions;
    public IReadOnlyList<DimensionData> DimensionResults => _dimensions;
    public IReadOnlyList<DimensionData> ExtractedDimensions => _dimensions;
    public string DimensionResultsHeading => "Derived from DXF drawing";
    public bool IsAuthorized => CanViewDimensions;
    public bool CanViewDimensions => _dimensionService?.CanViewDimensions == true;
    public bool CanExtractDimensions => _dimensionService?.CanExtractDimensions == true && DocumentType == CoreDocumentType.DxfDrawing && UploadStatus == CoreUploadStatus.Uploaded;
    public bool IsBusy { get => _isBusy; private set { if (_isBusy == value) return; _isBusy = value; Changed(); Changed(nameof(CanExtractDimensions)); ((RelayCommand)ExtractDimensionsCommand).Raise(); ((RelayCommand)OpenHistoricalStepCommand).Raise(); ((RelayCommand)OpenHistoricalDxfCommand).Raise(); ((RelayCommand)FlagFingerprintMismatchCommand).Raise(); ((RelayCommand)RejectFingerprintMismatchCommand).Raise(); ((RelayCommand)ReturnFingerprintMismatchCommand).Raise(); } }
    public string Message { get => _message; private set { if (_message == value) return; _message = value; Changed(); } }
    public string ErrorMessage { get => _errorMessage; private set { if (_errorMessage == value) return; _errorMessage = value; Changed(); } }
    public ICommand ExtractDimensionsCommand { get; }
    public ICommand ExtractCommand => ExtractDimensionsCommand;
    public ICommand LoadDimensionsCommand { get; }
    public ICommand RefreshDimensionsCommand => LoadDimensionsCommand;
    public IReadOnlyList<PriorVersionReview> PriorVersions => _priorVersions;
    public PriorVersionReview? SelectedPriorVersion { get => _selectedPriorVersion; private set { if (_selectedPriorVersion == value) return; _selectedPriorVersion = value; Changed(); ((RelayCommand)ComparePriorVersionCommand).Raise(); ((RelayCommand)ViewCurrentVersionCommand).Raise(); ((RelayCommand)OpenHistoricalStepCommand).Raise(); ((RelayCommand)OpenHistoricalDxfCommand).Raise(); } }
    public VersionComparison? VersionComparison => _versionComparison;
    public WorkflowStatus? PriorStatusFilter { get => _priorStatusFilter; set { _priorStatusFilter = value; Changed(); } }
    public DateTime? PriorFromUtc { get => _priorFromUtc; set { _priorFromUtc = value; Changed(); } }
    public DateTime? PriorToUtc { get => _priorToUtc; set { _priorToUtc = value; Changed(); } }
    public string? PriorMakerFilter { get => _priorMakerFilter; set { _priorMakerFilter = value; Changed(); } }
    public bool CanViewPriorVersions => _workflowService is not null && _authentication?.IsAuthenticated == true && _authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ViewPriorVersions);
    public ICommand LoadPriorVersionsCommand { get; }
    public ICommand SelectPriorVersionCommand { get; }
    public ICommand ComparePriorVersionCommand { get; }
    public ICommand ViewCurrentVersionCommand { get; }
    public ICommand OpenHistoricalStepCommand { get; }
    public ICommand OpenHistoricalDxfCommand { get; }
    public bool CanOpenHistoricalFiles => _documentService is not null && CanViewPriorVersions;
    public bool CanVerifyFingerprint => _workflowService is not null && _authentication?.IsAuthenticated == true && _authorization.IsAllowed(_authentication.CurrentUser, RolePermission.VerifyFingerprint) && _document is not null;
    public FingerprintVerificationResult? FingerprintVerification => _fingerprintVerification;
    public string FingerprintVerificationMessage => _fingerprintVerification?.Message ?? string.Empty;
    public string SubmittedMakerDisplayName => _submittedRevisionSnapshot?.MakerDisplayName ?? "Unavailable";
    public DateTime? SubmittedTimestampUtc => _submittedRevisionSnapshot?.SubmittedUtc;
    public string SubmittedFingerprintDisplay => _submittedRevisionSnapshot?.Package.Fingerprint?.Value ?? "Unavailable";
    public string SubmittedFingerprintAlgorithm => _submittedRevisionSnapshot?.Package.Fingerprint?.Algorithm ?? "Unavailable";
    public string SubmittedFingerprintCanonicalizationVersion => _submittedRevisionSnapshot?.Package.Fingerprint is { } fingerprint ? fingerprint.CanonicalizationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Unavailable";
    public string SubmittedSnapshotStatus => _submittedRevisionSnapshot is null ? "Unavailable" : "Loaded";
    public string SubmittedSnapshotError => _submittedSnapshotError ?? string.Empty;
    public ICommand VerifyFingerprintCommand { get; }
    public bool CanEscalateFingerprintMismatch => _workflowService is not null && _authentication?.IsAuthenticated == true && _authorization.IsAllowed(_authentication.CurrentUser, RolePermission.ReviewVerification) && _fingerprintVerification?.Status == FingerprintVerificationStatus.Mismatch && _document is not null;
    public ICommand FlagFingerprintMismatchCommand { get; }
    public ICommand RejectFingerprintMismatchCommand { get; }
    public ICommand ReturnFingerprintMismatchCommand { get; }
    public ICommand FlagForInvestigationCommand => FlagFingerprintMismatchCommand;
    public ICommand RejectForIntegrityCommand => RejectFingerprintMismatchCommand;
    public ICommand ReturnForIntegrityCommand => ReturnFingerprintMismatchCommand;
    public int HistoricalStepBytesRead { get; private set; }
    public int HistoricalDxfBytesRead { get; private set; }

    private void RaiseSubmissionRequested()
    {
        if (_document is null) return;
        SubmitRequested?.Invoke(_document.DocumentId);
        var provided = _submissionRequest;
        var request = (provided ?? new SubmissionRequest(_document.DocumentId)) with
        {
            StepDocumentVersionId = provided is not null && provided.StepDocumentVersionId != Guid.Empty ? provided.StepDocumentVersionId : (_document.DocumentType == CoreDocumentType.StepModel ? _document.Version?.VersionId ?? Guid.Empty : Guid.Empty),
            DxfDocumentId = provided is not null && provided.DxfDocumentId != Guid.Empty ? provided.DxfDocumentId : (_document.DocumentType == CoreDocumentType.DxfDrawing ? _document.DocumentId : Guid.Empty),
            DxfDocumentVersionId = provided is not null && provided.DxfDocumentVersionId != Guid.Empty ? provided.DxfDocumentVersionId : (_document.DocumentType == CoreDocumentType.DxfDrawing ? _document.Version?.VersionId ?? Guid.Empty : Guid.Empty),
            ExtractionCompleted = provided?.ExtractionCompleted == true || _document.GeometryStatus == DrawVerify.Core.Enums.GeometryStatus.Processed,
            DxfAvailable = provided?.DxfAvailable == true || _document.DocumentType == CoreDocumentType.DxfDrawing,
            VerificationCompleted = provided?.VerificationCompleted == true || _document.VerificationStatus == VerificationStatus.DimensionsExtracted,
            ExtractionEvidenceJson = !string.IsNullOrWhiteSpace(provided?.ExtractionEvidenceJson) && provided.ExtractionEvidenceJson != "{}" ? provided.ExtractionEvidenceJson : System.Text.Json.JsonSerializer.Serialize(new { documentId = _document.DocumentId, versionId = _document.Version?.VersionId, geometryStatus = _document.GeometryStatus }),
            VerificationEvidenceJson = !string.IsNullOrWhiteSpace(provided?.VerificationEvidenceJson) && provided.VerificationEvidenceJson != "{}" ? provided.VerificationEvidenceJson : System.Text.Json.JsonSerializer.Serialize(new { documentId = _document.DocumentId, versionId = _document.Version?.VersionId, verificationStatus = _document.VerificationStatus })
        };
        SubmissionRequested?.Invoke(request);
    }

    public void Load(Document document)
    {
        _snapshotLoadGeneration++;
        _submittedRevisionSnapshot = null;
        _submittedSnapshotError = null;
        _fingerprintVerification = null;
        Changed(nameof(SubmittedMakerDisplayName)); Changed(nameof(SubmittedTimestampUtc)); Changed(nameof(SubmittedFingerprintDisplay)); Changed(nameof(SubmittedFingerprintAlgorithm)); Changed(nameof(SubmittedFingerprintCanonicalizationVersion)); Changed(nameof(SubmittedSnapshotStatus)); Changed(nameof(SubmittedSnapshotError));
        Changed(nameof(FingerprintVerification)); Changed(nameof(FingerprintVerificationMessage)); Changed(nameof(CanEscalateFingerprintMismatch));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (!CanViewDimensions)
            _dimensions = Array.Empty<DimensionData>();
        Changed(nameof(Document));
        Changed(nameof(OriginalFileName)); Changed(nameof(FileSizeBytes)); Changed(nameof(UploadTimestampUtc));
        Changed(nameof(DocumentType)); Changed(nameof(UploadStatus)); Changed(nameof(VerificationStatus)); Changed(nameof(VerificationError));
        Changed(nameof(UploaderUserId)); Changed(nameof(UploaderDisplayName)); Changed(nameof(UploaderDisplayNameSnapshot));
        Changed(nameof(VerificationStatusDisplay)); Changed(nameof(GeometryStatus)); Changed(nameof(GeometryError)); Changed(nameof(CurrentVersion));
        Changed(nameof(Fingerprint)); Changed(nameof(FingerprintDisplay)); Changed(nameof(CanVerifyFingerprint));
        Changed(nameof(CanEscalateFingerprintMismatch));
        Changed(nameof(SubmittedMakerDisplayName)); Changed(nameof(SubmittedTimestampUtc)); Changed(nameof(SubmittedFingerprintDisplay));
        Changed(nameof(CanExtractDimensions)); Changed(nameof(IsAuthorized)); Changed(nameof(Dimensions));
        Changed(nameof(DimensionResults)); Changed(nameof(ExtractedDimensions));
        Changed(nameof(CanSubmitForReview));
        Changed(nameof(CanViewPriorVersions));
        ((RelayCommand)ExtractDimensionsCommand).Raise();
        ((RelayCommand)LoadDimensionsCommand).Raise();
        ((RelayCommand)SubmitForReviewCommand).Raise();
        ((RelayCommand)LoadPriorVersionsCommand).Raise();
        ((RelayCommand)VerifyFingerprintCommand).Raise();
        ((RelayCommand)FlagFingerprintMismatchCommand).Raise();
        ((RelayCommand)RejectFingerprintMismatchCommand).Raise();
        ((RelayCommand)ReturnFingerprintMismatchCommand).Raise();
        _ = LoadSubmittedRevisionSnapshotAsync(_document.DocumentId, _document.CurrentVersion, _snapshotLoadGeneration);
    }

    private async Task LoadSubmittedRevisionSnapshotAsync(Guid documentId, int revision, long generation)
    {
        if (_workflowService is null || !CanVerifyFingerprint) return;
        try
        {
            var snapshot = await _workflowService.GetPriorVersionAsync(documentId, revision).ConfigureAwait(true);
            if (_snapshotLoadGeneration != generation || _document?.DocumentId != documentId || _document.CurrentVersion != revision) return;
            _submittedRevisionSnapshot = snapshot;
            _submittedSnapshotError = snapshot is null ? "The submitted revision snapshot is unavailable." : null;
            Changed(nameof(SubmittedMakerDisplayName)); Changed(nameof(SubmittedTimestampUtc)); Changed(nameof(SubmittedFingerprintDisplay)); Changed(nameof(SubmittedFingerprintAlgorithm)); Changed(nameof(SubmittedFingerprintCanonicalizationVersion));
            Changed(nameof(SubmittedSnapshotStatus)); Changed(nameof(SubmittedSnapshotError));
        }
        catch (UnauthorizedAccessException exception)
        {
            if (_snapshotLoadGeneration != generation || _document?.DocumentId != documentId || _document.CurrentVersion != revision) return;
            _submittedRevisionSnapshot = null;
            _submittedSnapshotError = "The submitted revision snapshot is unavailable.";
            _logger?.Log(exception, "Fingerprint submission snapshot", $"Snapshot access was not authorized for documentId={documentId}, revision={revision}.");
            Changed(nameof(SubmittedMakerDisplayName)); Changed(nameof(SubmittedTimestampUtc)); Changed(nameof(SubmittedFingerprintDisplay)); Changed(nameof(SubmittedFingerprintAlgorithm)); Changed(nameof(SubmittedFingerprintCanonicalizationVersion)); Changed(nameof(SubmittedSnapshotStatus)); Changed(nameof(SubmittedSnapshotError));
        }
        catch (Exception exception)
        {
            if (_snapshotLoadGeneration != generation || _document?.DocumentId != documentId || _document.CurrentVersion != revision) return;
            _submittedRevisionSnapshot = null;
            _submittedSnapshotError = "The submitted revision snapshot is unavailable.";
            _logger?.Log(exception, "Fingerprint submission snapshot", $"Unable to load submitted snapshot for documentId={documentId}, revision={revision}.");
            Changed(nameof(SubmittedMakerDisplayName)); Changed(nameof(SubmittedTimestampUtc)); Changed(nameof(SubmittedFingerprintDisplay)); Changed(nameof(SubmittedFingerprintAlgorithm)); Changed(nameof(SubmittedFingerprintCanonicalizationVersion)); Changed(nameof(SubmittedSnapshotStatus)); Changed(nameof(SubmittedSnapshotError));
        }
    }

    private async Task VerifyFingerprintAsync()
    {
        if (!CanVerifyFingerprint || _workflowService is null || _document is null) return;
        IsBusy = true; ErrorMessage = string.Empty;
        try
        {
            _fingerprintVerification = await _workflowService.VerifyFingerprintAsync(_document.DocumentId, _document.CurrentVersion).ConfigureAwait(true);
            Message = _fingerprintVerification.Message;
            ErrorMessage = _fingerprintVerification.Status is FingerprintVerificationStatus.Mismatch or FingerprintVerificationStatus.Error ? _fingerprintVerification.Message : string.Empty;
            Changed(nameof(FingerprintVerification)); Changed(nameof(FingerprintVerificationMessage)); Changed(nameof(CanEscalateFingerprintMismatch));
            ((RelayCommand)FlagFingerprintMismatchCommand).Raise(); ((RelayCommand)RejectFingerprintMismatchCommand).Raise(); ((RelayCommand)ReturnFingerprintMismatchCommand).Raise();
        }
        catch (Exception exception)
        {
            _logger?.Log(exception, "Fingerprint verification", $"Unable to verify documentId={_document.DocumentId}, revision={_document.CurrentVersion}.");
            Message = ErrorMessage = "Unable to verify the fingerprint for this version.";
        }
        finally { IsBusy = false; }
    }

    private async Task FlagFingerprintMismatchAsync()
    {
        if (!CanEscalateFingerprintMismatch || _workflowService is null || _document is null) return;
        await RunFingerprintEscalationAsync(async () => await _workflowService.AddReviewCommentAsync(_document.DocumentId, _document.CurrentVersion, "Fingerprint Mismatch - Document May Have Been Altered. Flagged for investigation.").ConfigureAwait(true), "Fingerprint mismatch flagged for investigation.").ConfigureAwait(true);
    }

    private async Task DecideFingerprintMismatchAsync(ReviewDecisionType decisionType)
    {
        if (!CanEscalateFingerprintMismatch || _workflowService is null || _document is null) return;
        var note = "Fingerprint Mismatch - Document May Have Been Altered. Integrity verification failed.";
        await RunFingerprintEscalationAsync(async () => await _workflowService.DecideAsync(_document.DocumentId, _document.CurrentVersion, decisionType, note).ConfigureAwait(true), decisionType == ReviewDecisionType.Reject ? "Document rejected for fingerprint mismatch." : "Document returned for fingerprint investigation.").ConfigureAwait(true);
    }

    private async Task RunFingerprintEscalationAsync<T>(Func<Task<T>> action, string successMessage)
    {
        IsBusy = true; ErrorMessage = string.Empty;
        try { await action().ConfigureAwait(true); Message = successMessage; }
        catch (Exception exception)
        {
            if (_document is not null) _logger?.Log(exception, "Fingerprint mismatch escalation", $"Unable to escalate fingerprint mismatch for documentId={_document.DocumentId}, revision={_document.CurrentVersion}.");
            ErrorMessage = Message = "Unable to update the workflow for this fingerprint mismatch.";
        }
        finally { IsBusy = false; }
    }

    public async Task LoadPriorVersionsAsync()
    {
        if (!CanViewPriorVersions || _workflowService is null || _document is null) return;
        IsBusy = true; ErrorMessage = string.Empty;
        try
        {
            _priorVersions = await _workflowService.GetPriorVersionsAsync(_document.DocumentId, new PriorVersionFilter { Status = PriorStatusFilter, FromUtc = PriorFromUtc, ToUtc = PriorToUtc, MakerUserId = PriorMakerFilter }).ConfigureAwait(true);
            Message = string.Empty;
        }
        catch (UnauthorizedAccessException) { ErrorMessage = Message = "Only an authorized Checker or Approver can view prior versions."; }
        catch (Exception) { ErrorMessage = Message = "Unable to load prior versions."; }
        finally { Changed(nameof(PriorVersions)); IsBusy = false; }
    }

    private async Task SelectPriorVersionAsync(object? value)
    {
        if (value is not PriorVersionReview version || _workflowService is null) return;
        IsBusy = true; ErrorMessage = string.Empty;
        try { SelectedPriorVersion = await _workflowService.GetPriorVersionAsync(version.PackageId, version.Revision).ConfigureAwait(true); }
        catch (Exception) { ErrorMessage = Message = "Unable to load the selected prior version."; }
        finally { IsBusy = false; }
    }

    private async Task ComparePriorVersionAsync()
    {
        if (SelectedPriorVersion is null || _workflowService is null || _document is null) return;
        IsBusy = true;
        try { _versionComparison = await _workflowService.CompareVersionsAsync(SelectedPriorVersion.PackageId, _document.CurrentVersion, SelectedPriorVersion.Revision).ConfigureAwait(true); Changed(nameof(VersionComparison)); }
        catch (Exception) { ErrorMessage = Message = "Unable to compare the selected prior version."; }
        finally { IsBusy = false; }
    }

    private async Task OpenHistoricalFileAsync(bool dxf)
    {
        if (!CanOpenHistoricalFiles || SelectedPriorVersion is null || _documentService is null) return;
        IsBusy = true; ErrorMessage = string.Empty;
        try
        {
            var documentId = dxf ? SelectedPriorVersion.Package.DxfDocumentId : SelectedPriorVersion.Package.StepDocumentId;
            var versionId = dxf ? SelectedPriorVersion.DxfDocumentVersionId : SelectedPriorVersion.StepDocumentVersionId;
            if (documentId == Guid.Empty || versionId == Guid.Empty)
                throw new KeyNotFoundException($"The selected historical {(dxf ? "DXF" : "STEP")} document version is unavailable.");
            await using var stream = await _documentService.OpenHistoricalReadAsync(documentId, versionId).ConfigureAwait(true);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(true);
            if (dxf) HistoricalDxfBytesRead = checked((int)buffer.Length); else HistoricalStepBytesRead = checked((int)buffer.Length);
            Changed(nameof(HistoricalStepBytesRead)); Changed(nameof(HistoricalDxfBytesRead));
            Message = $"Opened historical {(dxf ? "DXF" : "STEP")} file ({buffer.Length} bytes) read-only.";
        }
        catch (UnauthorizedAccessException) { ErrorMessage = Message = "You are not authorized to open this historical file."; }
        catch (Exception) { ErrorMessage = Message = $"Unable to open the historical {(dxf ? "DXF" : "STEP")} file."; }
        finally { IsBusy = false; }
    }

    private void ViewCurrentVersion()
    {
        SelectedPriorVersion = null; _versionComparison = null; Changed(nameof(VersionComparison)); Message = "Viewing the current version.";
    }

    public async Task LoadDimensionsAsync()
    {
        if (_dimensionService is null || _document is null || !CanViewDimensions)
        {
            _dimensions = Array.Empty<DimensionData>();
            Changed(nameof(Dimensions)); Changed(nameof(DimensionResults)); Changed(nameof(ExtractedDimensions));
            return;
        }

        IsBusy = true;
        try
        {
            _dimensions = (await _dimensionService.GetDimensionsAsync(_document.DocumentId).ConfigureAwait(true)).ToArray();
            Message = string.Empty;
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            _dimensions = Array.Empty<DimensionData>();
            var safeMessage = exception switch
            {
                UnauthorizedAccessException => "The Maker or Checker role is required to view DXF dimensions.",
                KeyNotFoundException => "The requested document was not found.",
                InvalidOperationException invalidOperation when invalidOperation.Message == "Dimension results are available only for DXF drawings." => invalidOperation.Message,
                InvalidOperationException => "Unable to load DXF dimension results.",
                _ => "Unable to load DXF dimension results."
            };
            ErrorMessage = safeMessage;
            Message = safeMessage;
        }
        finally
        {
            Changed(nameof(Dimensions)); Changed(nameof(DimensionResults)); Changed(nameof(ExtractedDimensions));
            IsBusy = false;
        }
    }

    private async Task ExtractDimensionsAsync()
    {
        if (_dimensionService is null || _document is null || !CanExtractDimensions)
            return;
        IsBusy = true;
        Message = string.Empty;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _dimensionService.ExtractDimensionsAsync(_document.DocumentId).ConfigureAwait(true);
            if (result.Document is not null) _document = result.Document;
            _dimensions = result.Succeeded && CanViewDimensions ? result.Dimensions.ToArray() : Array.Empty<DimensionData>();
            var safeMessage = result.Succeeded ? result.Message : result.ErrorMessage ?? DxfDimensionExtractionService.GenericFailureMessage;
            Message = safeMessage;
            ErrorMessage = result.Succeeded ? string.Empty : safeMessage;
            Changed(nameof(Document)); Changed(nameof(VerificationStatus)); Changed(nameof(VerificationError)); Changed(nameof(VerificationStatusDisplay));
            Changed(nameof(Dimensions)); Changed(nameof(DimensionResults)); Changed(nameof(ExtractedDimensions));
        }
        catch (Exception exception)
        {
            _dimensions = Array.Empty<DimensionData>();
            var safeMessage = exception is UnauthorizedAccessException
                ? "The Maker or Checker role is required to extract DXF dimensions."
                : DxfDimensionExtractionService.GenericFailureMessage;
            ErrorMessage = safeMessage;
            Message = safeMessage;
            Changed(nameof(Dimensions)); Changed(nameof(DimensionResults)); Changed(nameof(ExtractedDimensions));
        }
        finally { IsBusy = false; }
    }

    private void Changed([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute, Predicate<object?> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
        public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
