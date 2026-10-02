using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;

public partial class StepPreviewView : UserControl
{
    private Point _lastMousePosition;
    private bool _isDragging;
    public StepPreviewView() => InitializeComponent();
    public event Action? BackRequested;
    private void BackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void ViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastMousePosition = e.GetPosition(StepViewport);
        StepViewport.CaptureMouse();
        e.Handled = true;
    }

    private void ViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        StepViewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || DataContext is not StepPreviewViewModel preview) return;
        var position = e.GetPosition(StepViewport);
        var delta = position - _lastMousePosition;
        _lastMousePosition = position;
        preview.Orbit(delta.X * 0.5, -delta.Y * 0.5);
    }

    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is StepPreviewViewModel preview)
            preview.Zoom(e.Delta > 0 ? 0.9 : 1.1);
        e.Handled = true;
    }
}
