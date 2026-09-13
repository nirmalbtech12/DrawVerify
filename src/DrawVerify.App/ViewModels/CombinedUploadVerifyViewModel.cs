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
    private readonly IDxfDrawingGenerationService? _generation;
    private ProcessingOperation? _processingCancellation;
    private ProcessingOperation? _stepProcessingCancellation;
    private ProcessingOperation? _dxfProcessingCancellation;
    private long _operationVersion;
    private Document? _stepDocument;
    private Document? _dxfDocument;
    private int _dxfDimensionCount;
    private bool _isProcessing;
    private Guid? _stepVersionId;
    private Guid? _dxfVersionId;

    public CombinedUploadVerifyViewModel(StepUploadViewModel stepUpload, DxfUploadViewModel dxfUpload,
        MeasurementExtractionService stepExtraction, DxfDimensionExtractionService dxfExtraction,
        VerificationViewModel verification, ICentralizedLogger? logger = null, IDxfDrawingGenerationService? generation = null)
    {
        StepUpload = stepUpload ?? throw new ArgumentNullException(nameof(stepUpload));
        DxfUpload = dxfUpload ?? throw new ArgumentNullException(nameof(dxfUpload));
        _stepExtraction = stepExtraction ?? throw new ArgumentNullException(nameof(stepExtraction));
        _dxfExtraction = dxfExtraction ?? throw new ArgumentNullException(nameof(dxfExtraction));
        Verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _logger = logger;
        _generation = generation;
        stepUpload.DocumentUploaded += OnStepUploaded;
        dxfUpload.DocumentUploaded += OnDxfUploaded;
        VerifyCommand = new RelayCommand(async _ => await VerifyAsync(), _ => CanVerify && !IsBusy);
        GenerateDxfCommand = new RelayCommand(async _ => await GenerateDxfAsync(), _ => CanGenerateDxf && !IsBusy);
        DownloadDxfCommand = new RelayCommand(async _ => await DownloadDxfAsync(), _ => CanDownloadDxf && !IsBusy);
        NewWorkflowCommand = new RelayCommand(_ => ResetWorkflow(), _ => !IsBusy);
    }

    public StepUploadViewModel StepUpload { get; }
    public DxfUploadViewModel DxfUpload { get; }
    public VerificationViewModel Verification { get; }
    public ICommand VerifyCommand { get; }
    public ICommand GenerateDxfCommand { get; }
    public ICommand DownloadDxfCommand { get; }
    public ICommand NewWorkflowCommand { get; }
    public string Step1Label => "Step 1: Upload 3D Model (STEP/STP)";
    public string Step2Label => "Step 2: Upload 2D Drawing (DXF)";
    public string GenerateDxfLabel => "Generate 2D Drawing";
    public string DownloadDxfLabel => "Download Generated DXF";
    public string UploadDxfLabel => "Upload Existing DXF";
    public string Step3Label => "Step 3: Verify";
    public string Step4Label => "Step 4: Comparison Results";
    public Document? StepDocument => _stepDocument;
    public Document? DxfDocument => _dxfDocument;
    public Guid? StepDocumentId => _stepDocument?.DocumentId;
    public Guid? DxfDocumentId => _dxfDocument?.DocumentId;
    public Guid? StepDocumentVersionId => _stepVersionId;
    public Guid? DxfDocumentVersionId => _dxfVersionId;
    public int ExtractedDxfDimensionCount => _dxfDimensionCount;
    public string StepStatus { get; private set; } = "Waiting for a STEP model.";
    public string DxfStatus { get; private set; } = "Waiting for a DXF drawing.";
    public string ProcessingStatus { get; private set; } = "Upload both files to begin processing.";
    public string ErrorMessage { get; private set; } = string.Empty;
    public bool IsBusy => _isProcessing || Verification.IsBusy || StepUpload.IsBusy || DxfUpload.IsBusy;
    public bool CanVerify => _stepDocument?.GeometryStatus == DrawVerify.Core.Enums.GeometryStatus.Processed &&
        _dxfDocument?.VerificationStatus == VerificationStatus.DimensionsExtracted && _dxfDimensionCount > 0 &&
        StepDocumentId is not null && DxfDocumentId is not null && _stepVersionId == _stepDocument?.Version?.VersionId && _dxfVersionId == _dxfDocument?.Version?.VersionId && Verification.CanMatchDimensions;
    public bool CanGenerateDxf => _generation?.CanGenerate == true && _stepDocument?.GeometryStatus == DrawVerify.Core.Enums.GeometryStatus.Processed && StepDocumentId is not null;
    public bool CanDownloadDxf => _generation is not null && DxfDocumentId is not null && DxfDocumentVersionId is not null;
    public Func<string, Stream, Task>? SaveDxfAsync { get; set; }
    public double GenerationProgress { get; private set; }
    public string GenerationStatus { get; private set; } = string.Empty;

    public event Action? StateChanged;
    public void AttachStepSelection(string name, Stream content) => StepUpload.Select(name, content);
    public void AttachDxfSelection(string name, Stream content) => DxfUpload.Select(name, content);

    private async void OnStepUploaded(Document document)
    {
        InvalidateProcessing();
        var operationVersion = _operationVersion;
        var operation = new ProcessingOperation();
        _stepProcessingCancellation = operation;
        _processingCancellation = operation;
        var cancellationToken = operation.Token;
        _stepDocument = document;
        _stepVersionId = document.Version?.VersionId;
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
        finally
        {
            if (ReferenceEquals(_stepProcessingCancellation, operation))
            {
                _stepProcessingCancellation = null;
                if (ReferenceEquals(_processingCancellation, operation)) _processingCancellation = null;
                _isProcessing = false;
                NotifyAll();
            }
            operation.Dispose();
        }
    }

    private async void OnDxfUploaded(Document document)
    {
        InvalidateProcessing();
        var operationVersion = _operationVersion;
        var operation = new ProcessingOperation();
        _dxfProcessingCancellation = operation;
        _processingCancellation = operation;
        var cancellationToken = operation.Token;
        _dxfDocument = document;
        _dxfVersionId = document.Version?.VersionId;
        DxfUpload.SetReplacementTarget(null);
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
        finally
        {
            if (ReferenceEquals(_dxfProcessingCancellation, operation))
            {
                _dxfProcessingCancellation = null;
                if (ReferenceEquals(_processingCancellation, operation)) _processingCancellation = null;
                _isProcessing = false;
                NotifyAll();
            }
            operation.Dispose();
        }
    }

    private async Task VerifyAsync()
    {
        if (!CanVerify) { ErrorMessage = "Verify is unavailable until both files are uploaded and processed successfully."; Notify(nameof(ErrorMessage)); return; }
        try
        {
            _isProcessing = true; ProcessingStatus = "Verification in progress…"; NotifyAll();
            if (!await Verification.SelectPairAsync(StepDocumentId!.Value, DxfDocumentId!.Value, StepDocumentVersionId!.Value, DxfDocumentVersionId!.Value)) throw new InvalidOperationException(Verification.MatchingStatus);
            if (Verification.SelectedSessionId is not Guid sessionId || !await Verification.MatchDimensionsAsync(sessionId)) throw new InvalidOperationException(Verification.MatchingStatus);
            ProcessingStatus = "Verification completed.";
        }
        catch (Exception exception) { _logger?.Log(exception, "Combined verification", $"Unable to verify stepDocumentId={StepDocumentId}; dxfDocumentId={DxfDocumentId}."); ProcessingStatus = "Verification failed."; ErrorMessage = exception.Message; }
        finally { _isProcessing = false; NotifyAll(); }
    }

    private async Task GenerateDxfAsync()
    {
        if (!CanGenerateDxf || _generation is null) return;
        var operation = new ProcessingOperation(); _processingCancellation = operation; _isProcessing = true;
        try
        {
            GenerationProgress = 0; GenerationStatus = "2D drawing generation started."; ErrorMessage = string.Empty; NotifyAll();
            var result = await _generation.GenerateAsync(StepDocumentId!.Value, operation.Token, new Progress<DxfDrawingGenerationProgress>(value => { GenerationProgress = value.Percent; GenerationStatus = value.Message; NotifyAll(); }));
            if (!result.Succeeded) { GenerationStatus = "2D drawing generation failed."; ErrorMessage = result.Message; }
            else { _dxfDocument = result.Document; _dxfVersionId = result.VersionId; _dxfDimensionCount = result.ExtractedDimensionCount; DxfUpload.SetReplacementTarget(result.Document?.DocumentId); DxfStatus = result.ExtractedDimensionCount == 0 ? "Generated DXF is visual-only. Download it for external dimensioning, then upload the dimensioned DXF to enable verification." : result.Diagnostics.Count == 0 ? $"Generated DXF is ready for download or external editing ({result.ExtractedDimensionCount} dimension(s))." : $"Generated DXF is ready with warnings: {string.Join("; ", result.Diagnostics)}"; GenerationStatus = result.Message; }
        }
        catch (Exception exception) { _logger?.Log(exception, "Combined DXF generation", $"stepDocumentId={StepDocumentId}"); GenerationStatus = "2D drawing generation failed."; ErrorMessage = exception.Message; }
        finally { if (ReferenceEquals(_processingCancellation, operation)) _processingCancellation = null; _isProcessing = false; operation.Dispose(); NotifyAll(); }
    }

    private async Task DownloadDxfAsync()
    {
        if (!CanDownloadDxf || _generation is null || SaveDxfAsync is null) return;
        try
        {
            await using var stream = await _generation.DownloadAsync(DxfDocumentId!.Value, DxfDocumentVersionId!.Value).ConfigureAwait(true);
            await SaveDxfAsync(_dxfDocument?.OriginalFileName ?? "drawing.dxf", stream).ConfigureAwait(true);
            GenerationStatus = "DXF download completed."; NotifyAll();
        }
        catch (Exception exception) { _logger?.Log(exception, "Combined DXF download", $"documentId={DxfDocumentId}; versionId={DxfDocumentVersionId}"); ErrorMessage = $"DXF download failed: {exception.Message}"; NotifyAll(); }
    }

    public void ResetWorkflow()
    {
        InvalidateProcessing();
        Verification.ResetWorkflowState();
        _stepDocument = null; _dxfDocument = null; _stepVersionId = null; _dxfVersionId = null; _dxfDimensionCount = 0; DxfUpload.SetReplacementTarget(null); StepStatus = "Waiting for a STEP model."; DxfStatus = "Waiting for a DXF drawing."; ProcessingStatus = "Upload both files to begin processing."; ErrorMessage = string.Empty;
        NotifyAll();
    }
    private void InvalidateProcessing()
    {
        _operationVersion++;
        var supersededStep = _stepProcessingCancellation;
        var supersededDxf = _dxfProcessingCancellation;
        var supersededShared = _processingCancellation;
        _stepProcessingCancellation = null;
        _dxfProcessingCancellation = null;
        _processingCancellation = null;
        supersededStep?.CancelAndDispose();
        if (!ReferenceEquals(supersededDxf, supersededStep)) supersededDxf?.CancelAndDispose();
        if (!ReferenceEquals(supersededShared, supersededStep) && !ReferenceEquals(supersededShared, supersededDxf)) supersededShared?.CancelAndDispose();
        _processingCancellation = null;
        _isProcessing = false;
    }
    private void NotifyAll() { foreach (var name in new[] { nameof(StepDocument), nameof(DxfDocument), nameof(StepDocumentId), nameof(DxfDocumentId), nameof(StepDocumentVersionId), nameof(DxfDocumentVersionId), nameof(ExtractedDxfDimensionCount), nameof(StepStatus), nameof(DxfStatus), nameof(ProcessingStatus), nameof(ErrorMessage), nameof(IsBusy), nameof(CanVerify), nameof(CanGenerateDxf), nameof(CanDownloadDxf), nameof(GenerationProgress), nameof(GenerationStatus) }) Notify(name); StateChanged?.Invoke(); ((RelayCommand)VerifyCommand).Raise(); ((RelayCommand)GenerateDxfCommand).Raise(); ((RelayCommand)DownloadDxfCommand).Raise(); ((RelayCommand)NewWorkflowCommand).Raise(); }
    private void Notify([CallerMemberName] string? name = null) => RaisePropertyChanged(name);
    public void Dispose() { InvalidateProcessing(); StepUpload.DocumentUploaded -= OnStepUploaded; DxfUpload.DocumentUploaded -= OnDxfUploaded; Verification.ResetWorkflowState(); Verification.Dispose(); GC.SuppressFinalize(this); }
    private sealed class ProcessingOperation : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private int _disposed;

        public CancellationToken Token => _source.Token;

        public void CancelAndDispose()
        {
            _source.Cancel();
            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _source.Dispose();
        }
    }
    private sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true; public void Execute(object? parameter) => execute(parameter); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
