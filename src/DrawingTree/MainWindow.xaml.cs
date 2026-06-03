using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Forms;
using DrawingTree.Logging;
using DrawingTree.Services;
using DrawingTree.Controls;

namespace DrawingTree;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly DrawingExtractor _drawingExtractor = new DrawingExtractor();
    private DrawingEditorControl? _drawingEditorControl;

    public MainWindow()
    {
        InitializeComponent();
        Logger.Instance.Info("MainWindow initialized");
    }

    /// <summary>
    /// Handle Search button click event
    /// </summary>
    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Search button clicked");
        // TODO: Implement search functionality
    }

    /// <summary>
    /// Handle Import Drawing button click event
    /// Show folder selection dialog, scan for PDF files, and display drawing editor
    /// </summary>
    private void ImportDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Import Drawing button clicked");

        // Show folder selection dialog
        using (var folderDialog = new FolderBrowserDialog())
        {
            folderDialog.ShowNewFolderButton = false;

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;
                Logger.Instance.Info($"Selected folder: {selectedPath}");

                // Scan folder for PDF files
                var drawings = _drawingExtractor.ScanFolder(selectedPath);

                if (drawings.Count == 0)
                {
                    System.Windows.MessageBox.Show(
                        "No PDF drawings found in the selected folder.",
                        "No Drawings Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // Create and display drawing editor control
                ShowDrawingEditor(drawings);
            }
            else
            {
                Logger.Instance.Info("Folder selection cancelled");
            }
        }
    }

    /// <summary>
    /// Handle Build Drawing Tree button click event
    /// </summary>
    private void BuildDrawingTreeButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Build Drawing Tree button clicked");
        // TODO: Implement build drawing tree functionality
    }

    /// <summary>
    /// Show drawing editor control in main display area
    /// </summary>
    /// <param name="drawings">List of drawings to edit</param>
    private void ShowDrawingEditor(System.Collections.Generic.List<Models.DrawingInfo> drawings)
    {
        // Clear main display area
        MainDisplayArea.Children.Clear();

        // Create drawing editor control
        _drawingEditorControl = new DrawingEditorControl();
        _drawingEditorControl.LoadDrawings(drawings);

        // Subscribe to events
        _drawingEditorControl.ReturnRequested += OnDrawingEditorReturn;

        // Add to main display area
        MainDisplayArea.Children.Add(_drawingEditorControl);

        Logger.Instance.Info("Drawing editor control displayed");
    }

    /// <summary>
    /// Handle drawing editor Return event
    /// Clear main display area and return to empty state
    /// </summary>
    private void OnDrawingEditorReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingEditorControl = null;
        Logger.Instance.Info("Returned to main view");
    }
}
