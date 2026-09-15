using System.Windows.Controls;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Views;

public partial class DocumentDetailView : UserControl
{
    public DocumentDetailView()
    {
        InitializeComponent();
    }

    private void PriorVersionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DocumentDetailViewModel viewModel && e.AddedItems.Count == 1 && viewModel.SelectPriorVersionCommand.CanExecute(e.AddedItems[0]))
            viewModel.SelectPriorVersionCommand.Execute(e.AddedItems[0]);
    }
}
