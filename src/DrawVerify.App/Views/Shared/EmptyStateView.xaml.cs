using System.Windows.Controls;
namespace DrawVerify.App.Views.Shared;
public partial class EmptyStateView : UserControl { public EmptyStateView() { InitializeComponent(); Measure(new System.Windows.Size(600, 200)); Arrange(new System.Windows.Rect(0, 0, 600, 200)); } }
