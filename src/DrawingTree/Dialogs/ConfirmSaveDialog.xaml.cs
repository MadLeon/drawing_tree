/// <summary>
/// ConfirmSaveDialog.xaml.cs
/// Shows a summary of pending tree changes (added/deleted/modified relationships)
/// and asks the user to confirm before writing to the database.
/// </summary>

using System.Text;
using System.Windows;
using DrawingTree.Data;

namespace DrawingTree.Dialogs;

public partial class ConfirmSaveDialog : Window
{
    /// <param name="summary">Computed diff between current tree and the database</param>
    public ConfirmSaveDialog(TreeChangeSummary summary)
    {
        InitializeComponent();

        AddedCount.Text    = summary.Added.ToString();
        DeletedCount.Text  = summary.Deleted.ToString();
        ModifiedCount.Text = summary.Modified.ToString();

        var sb = new StringBuilder();

        foreach (var item in summary.AddedItems)
            sb.AppendLine($"+ {item.ParentDrawingNumber} → {item.ChildDrawingNumber} rev {item.ChildRevision}  [added, qty: {item.Quantity}]");

        foreach (var item in summary.DeletedItems)
            sb.AppendLine($"- {item.ParentDrawingNumber} → {item.ChildDrawingNumber} rev {item.ChildRevision}  [deleted]");

        foreach (var item in summary.ModifiedItems)
            sb.AppendLine($"~ {item.ParentDrawingNumber} → {item.ChildDrawingNumber} rev {item.ChildRevision}  [qty: {item.OldQuantity} → {item.NewQuantity}]");

        ChangeDetail.Text = sb.ToString().TrimEnd();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)   => DialogResult = true;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
