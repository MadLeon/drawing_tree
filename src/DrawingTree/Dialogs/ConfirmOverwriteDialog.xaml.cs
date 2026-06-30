using System.Collections.Generic;
using System.Windows;
using DrawingTree.Models;

namespace DrawingTree.Dialogs;

/// <summary>
/// ConfirmOverwriteDialog.xaml.cs
/// Shows a side-by-side comparison of the row's current (UI) values versus the database values,
/// and asks the user to confirm before overwriting.
/// </summary>
public partial class ConfirmOverwriteDialog : Window
{
    /// <param name="current">Current (UI) values from the row being saved</param>
    /// <param name="db">Values currently stored in the database</param>
    public ConfirmOverwriteDialog(PartEditorRow current, DrawingInfo db)
    {
        InitializeComponent();

        var diffs = new List<DiffItem>();

        if (current.Revision != db.Revision)
            diffs.Add(new DiffItem("Revision", current.Revision, db.Revision));

        if (current.Description != db.Description)
            diffs.Add(new DiffItem("Description", current.Description, db.Description));

        if (current.IsAssembly != db.IsAssembly)
            diffs.Add(new DiffItem("Is Assembly",
                current.IsAssembly ? "Yes" : "No",
                db.IsAssembly ? "Yes" : "No"));

        if (current.PdfPath != db.PdfPath)
            diffs.Add(new DiffItem("File Path", current.PdfPath, db.PdfPath));

        DiffList.ItemsSource = diffs;
    }

    private void OverwriteButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelButton_Click(object sender, RoutedEventArgs e)    => DialogResult = false;
}

/// <param name="Field">Column name being compared</param>
/// <param name="Current">Value currently entered in the UI</param>
/// <param name="Database">Value currently stored in the database</param>
public record DiffItem(string Field, string Current, string Database);
