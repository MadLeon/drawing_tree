using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using DrawingTree.Models;

using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace DrawingTree.Dialogs;

/// <summary>
/// AddDrawingDialog.xaml.cs
/// Dialog for manually adding a drawing entry to the left panel.
/// Allows duplicate drawing numbers to support parts used in multiple positions.
/// </summary>
public partial class AddDrawingDialog : Window
{
    private readonly IReadOnlyList<DrawingInfo> _existingDrawings;

    /// <summary>The new DrawingInfo created by the user; null if cancelled.</summary>
    public DrawingInfo? Result { get; private set; }

    /// <param name="existingDrawings">Current left-panel drawings used for auto-fill lookup</param>
    public AddDrawingDialog(IReadOnlyList<DrawingInfo> existingDrawings)
    {
        InitializeComponent();
        _existingDrawings = existingDrawings;
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void DrawingNumberBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        AutoFillPathIfKnown(DrawingNumberBox.Text.Trim());
        UpdateConfirmState();
    }

    private void FilePathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateConfirmState();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title  = "Select PDF File",
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            FilePathBox.Text = dialog.FileName;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Result = new DrawingInfo
        {
            DrawingNumber = DrawingNumberBox.Text.Trim(),
            Revision      = RevisionBox.Text.Trim(),
            PdfPath       = FilePathBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// If the entered drawing number matches an existing entry, pre-fill the file path.
    /// </summary>
    private void AutoFillPathIfKnown(string drawingNumber)
    {
        if (string.IsNullOrEmpty(drawingNumber)) return;

        var match = _existingDrawings.FirstOrDefault(
            d => string.Equals(d.DrawingNumber, drawingNumber, System.StringComparison.OrdinalIgnoreCase));

        if (match != null && !string.IsNullOrEmpty(match.PdfPath))
            FilePathBox.Text = match.PdfPath;
    }

    /// <summary>Enables Confirm only when both required fields are filled.</summary>
    private void UpdateConfirmState()
    {
        ConfirmButton.IsEnabled =
            !string.IsNullOrWhiteSpace(DrawingNumberBox.Text) &&
            !string.IsNullOrWhiteSpace(FilePathBox.Text);
    }
}
