using System.Windows;
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Services;

using MessageBox            = System.Windows.MessageBox;
using MessageBoxButton      = System.Windows.MessageBoxButton;
using MessageBoxImage       = System.Windows.MessageBoxImage;
using FolderBrowserDialog   = System.Windows.Forms.FolderBrowserDialog;

namespace DrawingTree.Dialogs;

/// <summary>
/// NewCustomerDialog.xaml.cs
/// Dialog for creating a new customer, optionally setting its drawing/MP/bubble folder paths.
/// </summary>
public partial class NewCustomerDialog : Window
{
    private readonly PoRepository _repository;

    public NewCustomerDialog(PoRepository repository)
    {
        _repository = repository;
        InitializeComponent();
    }

    private void BrowseDrawingFolder_Click(object sender, RoutedEventArgs e)
        => BrowseInto(DrawingFolderBox);

    private void BrowseMpFolder_Click(object sender, RoutedEventArgs e)
        => BrowseInto(MpFolderBox);

    private void BrowseBubbleFolder_Click(object sender, RoutedEventArgs e)
        => BrowseInto(BubbleFolderBox);

    private static void BrowseInto(System.Windows.Controls.TextBox box)
    {
        using var dialog = new FolderBrowserDialog { ShowNewFolderButton = false };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            box.Text = dialog.SelectedPath;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Customer Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        int customerId = _repository.AddCustomer(name);
        if (customerId < 0)
        {
            MessageBox.Show("Failed to create customer. Check the log for details.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string drawingFolder = DrawingFolderBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(drawingFolder))
            _repository.UpsertFolderMapping(customerId, drawingFolder);

        string mpFolder = MpFolderBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(mpFolder))
            MpConfig.SetFolderName(name, mpFolder);

        string bubbleFolder = BubbleFolderBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(bubbleFolder))
            BubbleConfig.SetBubbleFolder(name, bubbleFolder);

        Logger.Instance.Info($"NewCustomerDialog: created customer id={customerId} name='{name}'");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
