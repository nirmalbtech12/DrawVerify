using System.IO;
using System.Windows;

namespace DrawVerify.App.Views;

public partial class LicenseViewerWindow : Window
{
    public LicenseViewerWindow()
    {
        InitializeComponent();

        var resource = System.Windows.Application.GetResourceStream(new Uri(
            "/DrawVerify.App;component/LICENSE.txt", UriKind.Relative));
        if (resource is null)
        {
            throw new InvalidOperationException("The embedded DrawVerify license resource is unavailable.");
        }

        using var reader = new StreamReader(resource.Stream);
        LicenseText.Text = reader.ReadToEnd();
    }
}
