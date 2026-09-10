using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawVerify.App.ViewModels;

public sealed class StepPreviewViewModel : INotifyPropertyChanged
{
    private string? _selectedFeatureReference;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? SelectedFeatureReference { get => _selectedFeatureReference; private set { _selectedFeatureReference = value; Notify(); } }
    public double? TargetX { get; private set; }
    public double? TargetY { get; private set; }
    public double? TargetZ { get; private set; }
    public bool IsFeatureHighlighted { get; private set; }
    public bool IsLocationAvailable => TargetX is not null && TargetY is not null && TargetZ is not null;
    public string SelectionStatus => SelectedFeatureReference is null ? "No STEP feature selected." : IsLocationAvailable ? $"Highlighted {SelectedFeatureReference}." : $"STEP feature {SelectedFeatureReference} has no valid preview location.";
    public void SelectFeature(string? reference, double? x, double? y, double? z)
    {
        SelectedFeatureReference = reference;
        TargetX = x; TargetY = y; TargetZ = z;
        IsFeatureHighlighted = !string.IsNullOrWhiteSpace(reference) && IsLocationAvailable;
        Notify(nameof(TargetX)); Notify(nameof(TargetY)); Notify(nameof(TargetZ)); Notify(nameof(IsLocationAvailable)); Notify(nameof(IsFeatureHighlighted)); Notify(nameof(SelectionStatus));
    }
    public void ClearSelection() => SelectFeature(null, null, null, null);
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
