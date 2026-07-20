using System.Windows;
using DrawingTree.Data;
using DrawingTree.Logging;

using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

namespace DrawingTree.Dialogs;

/// <summary>
/// EditChildPartDialog.xaml.cs
/// Dialog for editing an OE view child drawing row's own part (revision/description),
/// as opposed to EditItemDialog which edits the parent order_item.
/// </summary>
public partial class EditChildPartDialog : Window
{
    private readonly DrawingRepository _repository;
    private readonly int _partId;
    private bool   _isAssembly;
    private string _previousDrawingNumber = string.Empty;

    /// <summary>Saved revision; set only after a successful save.</summary>
    public string? SavedRevision { get; private set; }
    /// <summary>Saved description; set only after a successful save.</summary>
    public string? SavedDescription { get; private set; }

    public EditChildPartDialog(DrawingRepository repository, ChildDrawingRow child)
    {
        _repository = repository;
        _partId     = child.PartId;
        InitializeComponent();

        TitleBlock.Text = $"Edit Part / {child.DrawingNumber}";
        DrawingBox.Text = child.DrawingNumber;

        // Re-query the part table for is_assembly/previous_drawing_number so Save doesn't
        // silently clear them — ChildDrawingRow only carries the four fields OE view needs.
        var info = _repository.GetDrawingInfo(_partId);
        _isAssembly            = info?.IsAssembly ?? false;
        _previousDrawingNumber = info?.PreviousDrawingNumber ?? string.Empty;

        RevBox.Text  = info?.Revision    ?? child.Revision    ?? string.Empty;
        DescBox.Text = info?.Description ?? child.Description ?? string.Empty;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string rev  = RevBox.Text.Trim();
        string desc = DescBox.Text.Trim();

        bool ok = _repository.UpdatePart(_partId, rev, desc, _isAssembly, _previousDrawingNumber);
        if (!ok)
        {
            MessageBox.Show("Failed to save changes. Check the log for details.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"EditChildPartDialog: UpdatePart failed for partId={_partId}");
            return;
        }

        SavedRevision    = rev;
        SavedDescription = desc;
        Logger.Instance.Info($"EditChildPartDialog: updated part id={_partId}");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
