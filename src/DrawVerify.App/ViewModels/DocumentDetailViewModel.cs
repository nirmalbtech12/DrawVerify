using System.ComponentModel;
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

    public DocumentDetailViewModel()
    {
        ExtractDimensionsCommand = new RelayCommand(async _ => await ExtractDimensionsAsync().ConfigureAwait(true), _ => CanExtractDimensions && !IsBusy);
        LoadDimensionsCommand = new RelayCommand(async _ => await LoadDimensionsAsync().ConfigureAwait(true), _ => CanViewDimensions && !IsBusy);
    }

    public DocumentDetailViewModel(Document document) : this(document, null) { }

    public DocumentDetailViewModel(Document document, DxfDimensionExtractionService? dimensionService, IWorkflowService? workflowService = null, SubmissionRequest? submissionRequest = null, ISessionAuthenticationService? authentication = null, RoleAuthorization? authorization = null) : this()
    {
        _dimensionService = dimensionService; _workflowService = workflowService; _submissionRequest = submissionRequest; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization();
        SubmitForReviewCommand = new RelayCommand(_ => RaiseSubmissionRequested(), _ => CanSubmitForReview);
        Load(document);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Guid>? SubmitRequested;
    public event Action<SubmissionRequest>? SubmissionRequested;
    public ICommand SubmitForReviewCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
    public bool CanSubmitForReview => _workflowService is not null && _document is not null && (_authentication is null || (_authentication.IsAuthenticated && _authorization.IsAllowed(_authentication.CurrentUser, RolePermission.SubmitForReview)));
    public Document? Document => _document;
    public string OriginalFileName => _document?.OriginalFileName ?? string.Empty;
    public long FileSizeBytes => _document?.FileSizeBytes ?? 0;
    public DateTime UploadTimestampUtc => _document?.UploadTimestampUtc ?? default;
    public CoreDocumentType DocumentType => _document?.DocumentType ?? CoreDocumentType.Unknown;
    public CoreUploadStatus UploadStatus => _document?.UploadStatus ?? CoreUploadStatus.Pending;
    public VerificationStatus VerificationStatus => _document?.VerificationStatus ?? VerificationStatus.Unknown;
    public string? VerificationError => _document?.VerificationError;
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
    public bool IsBusy { get => _isBusy; private set { if (_isBusy == value) return; _isBusy = value; Changed(); Changed(nameof(CanExtractDimensions)); ((RelayCommand)ExtractDimensionsCommand).Raise(); } }
    public string Message { get => _message; private set { if (_message == value) return; _message = value; Changed(); } }
    public string ErrorMessage { get => _errorMessage; private set { if (_errorMessage == value) return; _errorMessage = value; Changed(); } }
    public ICommand ExtractDimensionsCommand { get; }
    public ICommand ExtractCommand => ExtractDimensionsCommand;
    public ICommand LoadDimensionsCommand { get; }
    public ICommand RefreshDimensionsCommand => LoadDimensionsCommand;

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
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (!CanViewDimensions)
            _dimensions = Array.Empty<DimensionData>();
        Changed(nameof(Document));
        Changed(nameof(OriginalFileName)); Changed(nameof(FileSizeBytes)); Changed(nameof(UploadTimestampUtc));
        Changed(nameof(DocumentType)); Changed(nameof(UploadStatus)); Changed(nameof(VerificationStatus)); Changed(nameof(VerificationError));
        Changed(nameof(UploaderUserId)); Changed(nameof(UploaderDisplayName)); Changed(nameof(UploaderDisplayNameSnapshot));
        Changed(nameof(VerificationStatusDisplay)); Changed(nameof(GeometryStatus)); Changed(nameof(GeometryError)); Changed(nameof(CurrentVersion));
        Changed(nameof(CanExtractDimensions)); Changed(nameof(IsAuthorized)); Changed(nameof(Dimensions));
        Changed(nameof(DimensionResults)); Changed(nameof(ExtractedDimensions));
        Changed(nameof(CanSubmitForReview));
        ((RelayCommand)ExtractDimensionsCommand).Raise();
        ((RelayCommand)LoadDimensionsCommand).Raise();
        ((RelayCommand)SubmitForReviewCommand).Raise();
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
