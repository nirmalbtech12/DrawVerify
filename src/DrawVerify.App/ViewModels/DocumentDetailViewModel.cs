namespace DrawVerify.App.ViewModels;
using System.Windows.Input;
using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;

public sealed class DocumentDetailViewModel
{
    private readonly MeasurementExtractionService? _measurements;
    private readonly IAuthenticationService? _authentication;
    private readonly IDocumentRepository? _documents;
    private bool _isExtracting;
    public Document? Document { get; private set; }
    public DocumentVersion? Version { get; private set; }
    public GeometryMeasurement? Geometry { get; private set; }
    public IReadOnlyList<FeatureMeasurement> Features { get; private set; } = Array.Empty<FeatureMeasurement>();
    public GeometryStatus GeometryStatus => IsAuthorized() ? Document?.GeometryStatus ?? GeometryStatus.NotExtracted : GeometryStatus.NotExtracted;
    public string? GeometryError => IsAuthorized() ? Document?.GeometryError : null;
    public bool IsExtracting => _isExtracting;
    public bool CanExtractMeasurements => !_isExtracting && IsAuthorized() && Document is { DocumentType: DocumentType.StepModel, UploadStatus: UploadStatus.Uploaded };
    public ICommand ExtractMeasurementsCommand { get; }
    public string? Feedback { get; private set; }

    public DocumentDetailViewModel(MeasurementExtractionService? measurements = null, IAuthenticationService? authentication = null, IDocumentRepository? documents = null)
    {
        _measurements = measurements; _authentication = authentication; _documents = documents;
        ExtractMeasurementsCommand = new RelayCommand(ExtractMeasurements, () => CanExtractMeasurements);
    }
    public void Load(Document document) { Document = document; Refresh(); NotifyState(); }
    public void Load(Document document, DocumentVersion version) { Document = document; Version = version; Refresh(); NotifyState(); }
    private void Refresh()
    {
        if (Document is null) return;
        if (!IsAuthorized())
        {
            Version = null;
            Geometry = null;
            Features = Array.Empty<FeatureMeasurement>();
            return;
        }
        Version ??= _documents?.GetCurrentVersion(Document.Id);
        if (Version is not null) { Geometry = _documents?.GetGeometry(Version.Id) ?? Version.Geometry; Features = _documents?.GetFeatures(Version.Id) ?? Version.Features; Document.GeometryStatus = Version.GeometryStatus; Document.GeometryError = Version.GeometryError; }
    }
    private void ExtractMeasurements()
    {
        if (_measurements is null || !CanExtractMeasurements) return;
        _isExtracting = true; Feedback = null; NotifyState();
        try { _measurements.Extract(Document!.Id); Refresh(); Feedback = GeometryStatus == GeometryStatus.Processed ? "Measurements extracted successfully." : GeometryError; }
        catch (Exception ex) { Feedback = ex.Message; if (Document is not null) { Document.GeometryStatus = GeometryStatus.Error; Document.GeometryError = ex.Message; } }
        finally { _isExtracting = false; NotifyState(); }
    }
    private void NotifyState() { (ExtractMeasurementsCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    private bool IsAuthorized() => _authentication?.CanExtractMeasurements == true && _authentication.CurrentUser is { IsAuthenticated: true } user &&
        (string.Equals(user.Role, "Maker", StringComparison.OrdinalIgnoreCase) || string.Equals(user.Role, "Checker", StringComparison.OrdinalIgnoreCase));

    private sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
    { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => canExecute(); public void Execute(object? parameter) => execute(); public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
