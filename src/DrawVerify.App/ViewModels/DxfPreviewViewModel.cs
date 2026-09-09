using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawVerify.App.ViewModels;

public sealed class DxfPreviewViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string? SelectedEntityReference { get; private set; }
    public string? SelectedAnchor { get; private set; }
    public bool IsHighlightVisible { get; private set; }
    public string PreviewStatus { get; private set; } = "No DXF dimension selected.";

    public bool SelectDimension(int entityReference, string? anchor)
    {
        SelectedEntityReference = entityReference.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectedAnchor = anchor;
        IsHighlightVisible = !string.IsNullOrWhiteSpace(anchor);
        PreviewStatus = IsHighlightVisible
            ? $"DXF dimension {SelectedEntityReference} highlighted and centered."
            : "Preview unavailable: this dimension has no stored DXF anchor.";
        Notify(nameof(SelectedEntityReference)); Notify(nameof(SelectedAnchor)); Notify(nameof(IsHighlightVisible)); Notify(nameof(PreviewStatus));
        return IsHighlightVisible;
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
