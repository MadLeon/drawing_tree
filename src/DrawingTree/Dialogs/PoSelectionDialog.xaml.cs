using System.Windows;
using System.Windows.Controls;

namespace DrawingTree.Dialogs;

/// <summary>
/// PoSelectionDialog.xaml.cs
/// Dialog for selecting a PO import JSON file before building the drawing tree.
/// </summary>
public partial class PoSelectionDialog : Window
{
    /// <summary>
    /// The selected JSON file name (e.g. "RT12345_import.json")
    /// </summary>
    public string? SelectedFile { get; private set; }

    public PoSelectionDialog(IEnumerable<string> importFiles)
    {
        InitializeComponent();
        PoComboBox.ItemsSource = importFiles.ToList();
    }

    private void PoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = PoComboBox.SelectedItem != null;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedFile = PoComboBox.SelectedItem as string;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
