using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Models;

namespace DrawVerify.App.Controls;

public partial class TemplatePlaceholderThumb : UserControl
{
    private Point _dragStart;
    private TemplatePlaceholderPosition? _positionStart;
    private bool _resizing;
    public TemplatePlaceholderThumb() { InitializeComponent(); MouseLeftButtonDown += Thumb_MouseLeftButtonDown; MouseMove += Thumb_MouseMove; MouseLeftButtonUp += Thumb_MouseLeftButtonUp; }
    public TemplatePlaceholder? Placeholder { get => DataContext as TemplatePlaceholder; set => DataContext = value; }

    private void Thumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Placeholder is null) return;
        _dragStart = e.GetPosition(FindCanvas()); _positionStart = Placeholder.Position; var local = e.GetPosition(this); _resizing = local.X >= ActualWidth - 16 && local.Y >= ActualHeight - 16;
        FindViewModel()?.SelectPlaceholder(Placeholder); CaptureMouse(); e.Handled = true;
    }

    private void Thumb_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || Placeholder is null || _positionStart is null) return;
        var canvas = FindCanvas(); var current = e.GetPosition(canvas); var dx = (current.X - _dragStart.X) / Math.Max(1, canvas.ActualWidth); var dy = (current.Y - _dragStart.Y) / Math.Max(1, canvas.ActualHeight);
        TryUpdate(_resizing ? _positionStart with { Width = _positionStart.Width + dx, Height = _positionStart.Height + dy } : _positionStart with { X = _positionStart.X + dx, Y = _positionStart.Y + dy });
    }

    private void Thumb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (IsMouseCaptured) ReleaseMouseCapture(); }

    private void TryUpdate(TemplatePlaceholderPosition position)
    { try { FindViewModel()?.SetPosition(Placeholder!, position); } catch (InvalidOperationException ex) { FindViewModel()?.ShowValidationError(ex.Message); } }
    private TemplateEditorViewModel? FindViewModel() { DependencyObject? current = this; while (current is not null) { if (current is FrameworkElement element && element.DataContext is TemplateEditorViewModel model) return model; current = VisualTreeHelper.GetParent(current); } return null; }
    private Canvas FindCanvas() { DependencyObject? current = this; while (current is not null) { if (current is Canvas canvas) return canvas; current = VisualTreeHelper.GetParent(current); } throw new InvalidOperationException("Placeholder is not hosted on an editor canvas."); }
}
