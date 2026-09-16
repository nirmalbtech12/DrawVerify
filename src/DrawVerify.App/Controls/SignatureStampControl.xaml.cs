using System.Windows.Controls;

namespace DrawVerify.App.Controls;

public partial class SignatureStampControl : UserControl
{
    public bool IsPreviewVisible => PreviewText.Visibility == System.Windows.Visibility.Visible;
    public SignatureStampControl()
    {
        InitializeComponent();
    }
    private void Preview_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TypedName.Text)) { PreviewText.Text = "Failed to capture signature. Please try again."; PreviewText.Visibility = System.Windows.Visibility.Visible; return; }
        PreviewText.Text = $"Preview: {TypedName.Text.Trim()}"; PreviewText.Visibility = System.Windows.Visibility.Visible;
    }
    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) { PreviewText.Visibility = System.Windows.Visibility.Collapsed; }
}
