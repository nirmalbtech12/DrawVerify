using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.App.ViewModels;

public sealed class CombinedUploadVerifyViewModel : TaskScreenViewModel, IDisposable
{
    private readonly MeasurementExtractionService _stepExtraction;
    private readonly DxfDimensionExtractionService _dxfExtraction;
    private readonly ICentralizedLogger? _logger;
    private CancellationTokenSource? _processingCancellation;
    private CancellationTokenSource? _stepProcessingCancellation;
    private CancellationTokenSource? _dxfProcessingCancellation;
    private long _operationVersion;
    private Document? _stepDocument;
    private Document? _dxfDocument;
    private int _dxfDimensionCount;
    private bool _isProcessing;

    public CombinedUploadVerifyViewModel(StepUploadViewModel stepUpload, DxfUploadViewModel dxfUpload,
        MeasurementExtractionService stepExtraction, DxfDimensionExtractionService dxfExtraction,
        VerificationViewModel verification, ICentralizedLogger? logger = null)
    {
        StepUpload = stepUpload ?? throw new ArgumentNullException(nameof(stepUpload));
        DxfUpload = dxfUpload ?? throw new ArgumentNullException(nameof(dxfUpload));
        _stepExtraction = stepExtraction ?? throw new ArgumentNullException(nameof(stepExtraction));
        _dxfExtraction = dxfExtraction ?? throw new ArgumentNullException(nameof(dxfExtraction));
        Verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _logger = logger;
        stepUpload.DocumentUploaded += OnStepUploaded;
        dxfUpload.DocumentUploaded += OnDxfUploaded;
        VerifyCommand = new RelayCommand(async _ => await VerifyAsync(), _ => CanVerify && !IsBusy);
        NewWorkflowCommand = new RelayCommand(_ => ResetWorkflow(), _ => !IsBusy);
    }

    public StepUploadViewModel StepUpload { get; }
    public DxfUploadViewModel DxfUpload { get; }
    public VerificationViewModel Verification { get; }
    public ICommand VerifyCommand { get; }
    public ICommand NewWorkflowCommand { get; }
    public string Step1Label => "Step 1: Upload 3D Model (STEP/STP)";
    public string Step2Label => "Step 2: Upload 2D Drawing (DXF)";
    public string Step3Label => "Step 3: Verify";
    public string Step4Label => "Step 4: Comparison Results";
    public Document? StepDocument => _stepDocument;
    public Document? DxfDocument => _dxfDocument;
    public Guid? StepDocumentId => _stepDocument?.DocumentId;
    public Guid? DxfDocumentId => _dxfDocument?.DocumentId;
    public int ExtractedDxfDimensionCount => _dxfDimensionCount;
    public string StepStatus { get; private set; } = "Waiting for a STEP model.";
    public string DxfStatus { get; private set; } = "Waiting for a DXF drawing.";
    public string ProcessingStatus { get; private set; } = "Upload both files to begin processing.";
    public string ErrorMessage { get; private set; } = string.Empty;
    public bool IsBusy => _isProcessing || Verification.IsBusy || StepUpload.IsBusy || DxfUpload.IsBusy;
    public bool CanVerify => _stepDocument?.GeometryStatus == DrawVerify.Core.Enums.GeometryStatus.Processed &&
        _dxfDocument?.VerificationStatus == VerificationStatus.DimensionsExtracted && _dxfDimensionCount > 0 &&
        StepDocumentId is not null && DxfDocumentId is not null && Verification.CanMatchDimensions;

    public event Action? StateChanged;
    public void AttachStepSelection(string name, Stream content) => StepUpload.Select(name, content);
    public void AttachDxfSelection(string name, Stream content) => DxfUpload.Select(name, content);

    private async void OnStepUploaded(Document document)
    {
        InvalidateProcessing();
        var operationVersion = _operationVersion;
        _stepProcessingCancellation = new CancellationTokenSource();
        _processingCancellation = _stepProcessingCancellation;
        var cancellationToken = _stepProcessingCancellation.Token;
        _stepDocument = document;
        StepStatus = "STEP uploaded; processing geometry…";
        ErrorMessage = string.Empty;
        NotifyAll();
        try
        {
            _isProcessing = true; NotifyAll();
            var result = await Task.Run(() => _stepExtraction.Extract(document.DocumentId, cancellationToken), cancellationToken);
            if (operationVersion != _operationVersion || cancellationToken.IsCancellationRequested) return;
            _stepDocument = result.Succeeded ? document with { GeometryStatus = DrawVerify.Core.Enums.GeometryStatus.Processed, GeometryError = null } : document with { GeometryStatus = DrawVerify.Core.Enums.GeometryStatus.Error, GeometryError = result.ErrorMessage };
            StepStatus = result.Succeeded ? "STEP geometry ready." : "STEP processing failed.";
            if (!result.Succeeded) ErrorMessage = result.ErrorMessage ?? "STEP processing failed. Verification remains disabled.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (operationVersion == _operationVersion) { _logger?.Log(exception, "Combined STEP processing", $"Unable to process documentId={document.DocumentId}."); StepStatus = "STEP processing failed."; ErrorMessage = $"STEP processing failed: {exception.Message}"; }
        finally { if (ReferenceEquals(_stepProcessingCancellation, _processingCancellation)) _processingCancellation = null; _stepProcessingCancellation?.Dispose(); _stepProcessingCancellation = null; _isProcessing = false; if (operationVersion == _operationVersion) NotifyAll(); }
    }

    private async void OnDxfUploaded(Document document)
    {
        InvalidateProcessing();
        var operationVersion = _operationVersion;
        _dxfProcessingCancellation = new CancellationTokenSource();
        _processingCancellation = _dxfProcessingCancellation;
        var cancellationToken = _dxfProcessingCancellation.Token;
        _dxfDocument = document;
        DxfStatus = "DXF uploaded; extracting dimensions…";
        ErrorMessage = string.Empty;
        NotifyAll();
        try
        {
            _isProcessing = true; NotifyAll();
            var result = await _dxfExtraction.ExtractAsync(document.DocumentId, cancellationToken);
            if (operationVersion != _operationVersion || cancellationToken.IsCancellationRequested) return;
            _dxfDocument = result.Document ?? document;
            _dxfDimensionCount = result.Dimensions.Count;
            DxfStatus = result.Dimensions.Count == 0 ? "No supported dimensions found in the DXF drawing." : $"DXF ready: {result.Dimensions.Count} dimension(s) extracted.";
            if (result.Dimensions.Count == 0 || result.Document?.VerificationStatus != VerificationStatus.DimensionsExtracted)
                ErrorMessage = result.Message ?? "DXF extraction did not produce dimensions. Verification remains disabled.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (operationVersion == _operationVersion) { _logger?.Log(exception, "Combined DXF extraction", $"Unable to extract documentId={document.DocumentId}."); DxfStatus = "DXF extraction failed."; ErrorMessage = $"DXF extraction failed: {exception.Message}"; }
        finally { if (ReferenceEquals(_dxfProcessingCancellation, _processingCancellation)) _processingCancellation = null; _dxfProcessingCancellation?.Dispose(); _dxfProcessingCancellation = null; _isProcessing = false; if (operationVersion == _operationVersion) NotifyAll(); }
    }

    private async Task VerifyAsync()
    {
        if (!CanVerify) { ErrorMessage = "Verify is unavailable until both files are uploaded and processed successfully."; Notify(nameof(ErrorMessage)); return; }
        try
        {
            _isProcessing = true; ProcessingStatus = "Verification in progress…"; NotifyAll();
            if (!await Verification.SelectPairAsync(StepDocumentId!.Value, DxfDocumentId!.Value)) throw new InvalidOperationException(Verification.MatchingStatus);
            if (Verification.SelectedSessionId is not Guid sessionId || !await Verification.MatchDimensionsAsync(sessionId)) throw new InvalidOperationException(Verification.MatchingStatus);
            ProcessingStatus = "Verification completed.";
        }
        catch (Exception exception) { _logger?.Log(exception, "Combined verification", $"Unable to verify stepDocumentId={StepDocumentId}; dxfDocumentId={DxfDocumentId}."); ProcessingStatus = "Verification failed."; ErrorMessage = exception.Message; }
        finally { _isProcessing = false; NotifyAll(); }
    }

    public void ResetWorkflow()
    {
        InvalidateProcessing();
        Verification.ResetWorkflowState();
        _stepDocument = null; _dxfDocument = null; _dxfDimensionCount = 0; StepStatus = "Waiting for a STEP model."; DxfStatus = "Waiting for a DXF drawing."; ProcessingStatus = "Upload both files to begin processing."; ErrorMessage = string.Empty;
        NotifyAll();
    }
    private void InvalidateProcessing()
    {
        _operationVersion++;
        _stepProcessingCancellation?.Cancel();
        _dxfProcessingCancellation?.Cancel();
        _processingCancellation?.Cancel();
        _stepProcessingCancellation?.Dispose(); _stepProcessingCancellation = null;
        _dxfProcessingCancellation?.Dispose(); _dxfProcessingCancellation = null;
        _processingCancellation = null;
    }
    private void NotifyAll() { foreach (var name in new[] { nameof(StepDocument), nameof(DxfDocument), nameof(StepDocumentId), nameof(DxfDocumentId), nameof(ExtractedDxfDimensionCount), nameof(StepStatus), nameof(DxfStatus), nameof(ProcessingStatus), nameof(ErrorMessage), nameof(IsBusy), nameof(CanVerify) }) Notify(name); StateChanged?.Invoke(); ((RelayCommand)VerifyCommand).Raise(); ((RelayCommand)NewWorkflowCommand).Raise(); }
    private void Notify([CallerMemberName] string? name = null) => RaisePropertyChanged(name);
    public void Dispose() { InvalidateProcessing(); StepUpload.DocumentUploaded -= OnStepUploaded; DxfUpload.DocumentUploaded -= OnDxfUploaded; Verification.ResetWorkflowState(); Verification.Dispose(); GC.SuppressFinalize(this); }
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
