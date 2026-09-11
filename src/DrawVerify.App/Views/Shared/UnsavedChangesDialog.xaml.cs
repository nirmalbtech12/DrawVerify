using System.Windows;
namespace DrawVerify.App.Views.Shared;
public partial class UnsavedChangesDialog : Window { public UnsavedChangesDialog() => InitializeComponent(); private void KeepEditing(object s, RoutedEventArgs e) { DialogResult = false; } private void Discard(object s, RoutedEventArgs e) { DialogResult = true; } }
