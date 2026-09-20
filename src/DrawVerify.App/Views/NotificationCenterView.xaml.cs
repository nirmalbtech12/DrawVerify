using System.Windows.Controls;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;

public partial class NotificationCenterView : UserControl
{
    public NotificationCenterView() { InitializeComponent(); Loaded += async (_, _) => await ((NotificationCenterViewModel)DataContext).LoadAsync(); }
}
