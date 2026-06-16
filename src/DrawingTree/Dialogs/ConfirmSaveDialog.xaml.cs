/// <summary>
/// ConfirmSaveDialog.xaml.cs
/// Shows a summary of pending tree changes (added/deleted/modified relationships)
/// and asks the user to confirm before writing to the database.
/// </summary>

using System.Windows;
using DrawingTree.Data;

namespace DrawingTree.Dialogs;

public partial class ConfirmSaveDialog : Window
{
    /// <param name="summary">Computed diff between current tree and the database</param>
    public ConfirmSaveDialog(TreeChangeSummary summary)
    {
        InitializeComponent();

        AddedCount.Text   = summary.Added.ToString();
        DeletedCount.Text = summary.Deleted.ToString();
        ModifiedCount.Text = summary.Modified.ToString();

        if (summary.DeletedItems.Count > 0)
        {
            DeletedList.ItemsSource = summary.DeletedItems;
        }
        else
        {
            DeletedSection.Visibility = Visibility.Collapsed;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)   => DialogResult = true;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
