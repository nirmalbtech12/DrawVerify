using System.ComponentModel;
using System.Runtime.CompilerServices;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.App.ViewModels;

/// <summary>UI-facing navigation state for a STEP feature; native model ownership remains in OcctService.</summary>
public sealed class StepPreviewViewModel : INotifyPropertyChanged
{
    private readonly ICentralizedLogger? _logger;
    public StepPreviewViewModel(ICentralizedLogger? logger = null) => _logger = logger;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? HighlightedFeatureReference { get; private set; }
    public double? HighlightedLocationX { get; private set; }
    public double? HighlightedLocationY { get; private set; }
    public bool IsPreviewAvailable => !string.IsNullOrWhiteSpace(HighlightedFeatureReference);
    public string PreviewStatus => IsPreviewAvailable ? $"Highlighted STEP feature {HighlightedFeatureReference}." : "STEP preview location unavailable.";
    public double PreviewCanvasX => HighlightedLocationX is double x && double.IsFinite(x) ? Math.Clamp(150 + x * 8, 12, 268) : 150;
    public double PreviewCanvasY => HighlightedLocationY is double y && double.IsFinite(y) ? Math.Clamp(90 - y * 8, 12, 168) : 90;
    public double CameraZoom { get; private set; } = 1;
    public string CameraStatus => IsPreviewAvailable ? $"Fit and zoom 3D view to {HighlightedFeatureReference}." : "3D camera navigation unavailable.";

    public bool HighlightAndFit(string? featureReference, double? locationX, double? locationY)
    {
        if (string.IsNullOrWhiteSpace(featureReference))
        {
            HighlightedFeatureReference = null;
            HighlightedLocationX = locationX;
            HighlightedLocationY = locationY;
            CameraZoom = 1;
            _logger?.Log(new InvalidOperationException("The selected STEP feature has no navigable reference."), "STEP preview navigation", "Unable to highlight and fit the selected extra dimension.");
        }
        else
        {
            HighlightedFeatureReference = featureReference;
            HighlightedLocationX = locationX;
            HighlightedLocationY = locationY;
            CameraZoom = 2;
        }
        Notify(nameof(HighlightedFeatureReference)); Notify(nameof(HighlightedLocationX)); Notify(nameof(HighlightedLocationY)); Notify(nameof(IsPreviewAvailable)); Notify(nameof(PreviewStatus)); Notify(nameof(PreviewCanvasX)); Notify(nameof(PreviewCanvasY)); Notify(nameof(CameraZoom)); Notify(nameof(CameraStatus));
        return IsPreviewAvailable;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
