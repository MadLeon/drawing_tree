using System.IO;
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
using DrawingTree.Dialogs;

namespace DrawingTree;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly DrawingExtractor _drawingExtractor = new DrawingExtractor();
    private DrawingEditorControl? _drawingEditorControl;
    private TreeBuilderControl? _treeBuilderControl;
    private DrawingViewerControl? _drawingViewerControl;

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
    /// Handle Build Drawing Tree button click event.
    /// Checks for import JSON files, prompts PO selection, then shows the tree builder.
    /// </summary>
    private void BuildDrawingTreeButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Build Drawing Tree button clicked");

        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        var importFiles = Directory.GetFiles(appDir, "*_import.json")
                                   .Select(f => Path.GetFileName(f))
                                   .OrderBy(f => f)
                                   .ToList();

        if (importFiles.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No import files found.\nPlease use \"Import Drawing\" first to generate a drawing list.",
                "No Import Files",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Logger.Instance.Warning("Build Drawing Tree: no *_import.json files found");
            return;
        }

        var dialog = new PoSelectionDialog(importFiles) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedFile == null) return;

        string selectedPath = Path.Combine(appDir, dialog.SelectedFile);
        ShowTreeBuilder(selectedPath);
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
    /// Show tree builder control for the given import file
    /// </summary>
    /// <param name="importFilePath">Full path to the *_import.json file</param>
    private void ShowTreeBuilder(string importFilePath)
    {
        MainDisplayArea.Children.Clear();

        _treeBuilderControl = new TreeBuilderControl();
        _treeBuilderControl.LoadFromJsonFile(importFilePath);
        _treeBuilderControl.ReturnRequested += OnTreeBuilderReturn;

        MainDisplayArea.Children.Add(_treeBuilderControl);
        Logger.Instance.Info($"Tree builder displayed for {importFilePath}");
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

    /// <summary>
    /// Handle tree builder Return event
    /// </summary>
    private void OnTreeBuilderReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _treeBuilderControl = null;
        Logger.Instance.Info("Returned to main view from tree builder");
    }

    /// <summary>
    /// Handle View Drawings button click.
    /// Scans for *_tree.json files and shows the viewer for the selected tree.
    /// </summary>
    private void ViewDrawingsButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("View Drawings button clicked");

        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        var treeFiles = Directory.GetFiles(appDir, "*_tree.json")
                                 .Select(f => Path.GetFileName(f))
                                 .OrderBy(f => f)
                                 .ToList();

        if (treeFiles.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No tree files found.\nPlease use \"Build Drawing Tree\" first to create a tree.",
                "No Tree Files",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Logger.Instance.Warning("View Drawings: no *_tree.json files found");
            return;
        }

        var dialog = new PoSelectionDialog(treeFiles) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedFile == null) return;

        string selectedPath = Path.Combine(appDir, dialog.SelectedFile);
        ShowDrawingViewer(selectedPath);
    }

    /// <summary>
    /// Show drawing viewer control for the given tree JSON file
    /// </summary>
    /// <param name="treeFilePath">Full path to the *_tree.json file</param>
    private void ShowDrawingViewer(string treeFilePath)
    {
        MainDisplayArea.Children.Clear();

        _drawingViewerControl = new DrawingViewerControl();
        _drawingViewerControl.LoadFromJsonFile(treeFilePath);
        _drawingViewerControl.ReturnRequested += OnDrawingViewerReturn;

        MainDisplayArea.Children.Add(_drawingViewerControl);
        Logger.Instance.Info($"Drawing viewer displayed for {treeFilePath}");
    }

    /// <summary>
    /// Handle drawing viewer Return event
    /// </summary>
    private void OnDrawingViewerReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingViewerControl = null;
        Logger.Instance.Info("Returned to main view from drawing viewer");
    }
}
