using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawVerify.App.ViewModels;

public sealed class DxfPreviewViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? SelectedEntityReference { get; private set; }
    public string? SelectedAnchor { get; private set; }
    public double? SelectedX { get; private set; }
    public double? SelectedY { get; private set; }
    public string? NavigationTarget { get; private set; }
    public bool IsHighlightVisible { get; private set; }
    public string PreviewStatus { get; private set; } = "No DXF dimension selected.";

    public bool SelectDimension(int entityReference, string? anchor)
        => SelectDimension(entityReference, anchor, null, null);

    public bool SelectDimension(int entityReference, string? anchor, double? x, double? y)
    {
        SelectedEntityReference = entityReference.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectedAnchor = anchor;
        SelectedX = x; SelectedY = y;
        NavigationTarget = x is double && y is double ? $"{x.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{y.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" : anchor;
        IsHighlightVisible = !string.IsNullOrWhiteSpace(anchor) || x is not null && y is not null;
        PreviewStatus = IsHighlightVisible
            ? $"DXF dimension {SelectedEntityReference} highlighted and centered."
            : "Preview unavailable: this dimension has no stored DXF anchor.";
        Notify(nameof(SelectedEntityReference)); Notify(nameof(SelectedAnchor)); Notify(nameof(IsHighlightVisible)); Notify(nameof(PreviewStatus));
        Notify(nameof(SelectedX)); Notify(nameof(SelectedY)); Notify(nameof(NavigationTarget));
        return IsHighlightVisible;
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
