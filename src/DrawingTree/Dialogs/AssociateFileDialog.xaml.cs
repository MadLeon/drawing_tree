/// <summary>
/// AssociateFileDialog.xaml.cs
/// Confirmation dialog shown before the file picker of a manual file association.
/// Replaces a plain Yes/No message box so the user can also decide, up front,
/// whether the file about to be picked becomes the section's active file.
/// </summary>
/// <remarks>
/// Usage:
/// - Show it, and when ShowDialog() returns true open the file picker
/// - Read SetAsActive to decide whether the new record is marked active
/// </remarks>

using System.Windows;

namespace DrawingTree.Dialogs;

public partial class AssociateFileDialog : Window
{
    /// <summary>True when the user asked for the picked file to become the active one.</summary>
    public bool SetAsActive { get; private set; }

    /// <summary>
    /// Builds the dialog.
    /// </summary>
    /// <param name="title">Window caption</param>
    /// <param name="message">Body text explaining what will be associated</param>
    /// <param name="setActiveByDefault">Initial state of the "set as active" checkbox</param>
    public AssociateFileDialog(string title, string message, bool setActiveByDefault)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        SetActiveCheck.IsChecked = setActiveByDefault;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        SetAsActive = SetActiveCheck.IsChecked == true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
