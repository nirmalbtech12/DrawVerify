using System.ComponentModel;
using System.Runtime.CompilerServices;
using DrawVerify.Core.Models;

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
    public InternalApprovalStamp? InternalApprovalStamp { get; private set; }
    public bool HasInternalApprovalStamp => InternalApprovalStamp is not null;
    public string ApprovalStampText => InternalApprovalStamp is null ? string.Empty : $"Internally Approved · {InternalApprovalStamp.ApproverDisplayName} · {InternalApprovalStamp.AppliedUtc:O} · Revision {InternalApprovalStamp.Revision} · {InternalApprovalStamp.StampId} · {InternalApprovalStamp.FingerprintReference} · {InternalApprovalStamp.VerificationStatus}";
    public void SetInternalApprovalStamp(InternalApprovalStamp? stamp) { stamp?.Validate(); InternalApprovalStamp = stamp; Notify(nameof(InternalApprovalStamp)); Notify(nameof(HasInternalApprovalStamp)); Notify(nameof(ApprovalStampText)); }

    public bool SelectDimension(int entityReference, string? anchor)
        => SelectDimension(entityReference, anchor, null, null);

    public bool SelectDimension(int entityReference, string? anchor, double? x, double? y)
    {
        if (entityReference <= 0 || (string.IsNullOrWhiteSpace(anchor) && (x is null || y is null)))
        {
            SelectedEntityReference = null;
            SelectedAnchor = null;
            SelectedX = null;
            SelectedY = null;
            NavigationTarget = null;
            IsHighlightVisible = false;
            PreviewStatus = "Preview unavailable: the DXF entity or anchor could not be resolved.";
            Notify(nameof(SelectedEntityReference)); Notify(nameof(SelectedAnchor)); Notify(nameof(SelectedX)); Notify(nameof(SelectedY)); Notify(nameof(NavigationTarget)); Notify(nameof(IsHighlightVisible)); Notify(nameof(PreviewStatus));
            return false;
        }
        SelectedEntityReference = entityReference.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectedAnchor = anchor;
        SelectedX = x; SelectedY = y;
        NavigationTarget = x is double && y is double ? $"{x.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{y.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" : anchor;
        IsHighlightVisible = true;
        PreviewStatus = IsHighlightVisible
            ? $"DXF dimension {SelectedEntityReference} highlighted and centered."
            : "Preview unavailable: this dimension has no stored DXF anchor.";
        Notify(nameof(SelectedEntityReference)); Notify(nameof(SelectedAnchor)); Notify(nameof(IsHighlightVisible)); Notify(nameof(PreviewStatus));
        Notify(nameof(SelectedX)); Notify(nameof(SelectedY)); Notify(nameof(NavigationTarget));
        return IsHighlightVisible;
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
