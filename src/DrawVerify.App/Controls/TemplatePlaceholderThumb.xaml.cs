using System.Windows.Controls;
using DrawVerify.Core.Models;

namespace DrawVerify.App.Controls;

public partial class TemplatePlaceholderThumb : UserControl
{
    public TemplatePlaceholderThumb() => InitializeComponent();
    public TemplatePlaceholder? Placeholder { get => DataContext as TemplatePlaceholder; set => DataContext = value; }
}
