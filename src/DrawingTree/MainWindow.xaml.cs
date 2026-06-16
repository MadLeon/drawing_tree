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
    private PartEditorControl? _partEditorControl;

    private string ImportsDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "imports");

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

        string importsDir = ImportsDir;
        if (!Directory.Exists(importsDir)) Directory.CreateDirectory(importsDir);
        var importFiles = Directory.GetFiles(importsDir, "*_import.json")
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

        string selectedPath = Path.Combine(importsDir, dialog.SelectedFile);
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
    /// Handle Edit Part button click.
    /// Scans imports folder for *_import.json files, prompts PO selection,
    /// then shows the part editor populated with DB metadata for each drawing.
    /// </summary>
    private void EditPartButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Edit Part button clicked");

        string importsDir = ImportsDir;
        if (!Directory.Exists(importsDir)) Directory.CreateDirectory(importsDir);
        var importFiles = Directory.GetFiles(importsDir, "*_import.json")
                                   .Select(f => Path.GetFileName(f))
                                   .OrderBy(f => f)
                                   .ToList();

        if (importFiles.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No import files found.\nPlease use \"Import Drawing\" first.",
                "No Import Files",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Logger.Instance.Warning("Edit Part: no *_import.json files found");
            return;
        }

        var dialog = new PoSelectionDialog(importFiles) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedFile == null) return;

        ShowPartEditor(Path.Combine(importsDir, dialog.SelectedFile));
    }

    /// <summary>
    /// Show the part editor control for the given import file path.
    /// </summary>
    private void ShowPartEditor(string importFilePath)
    {
        MainDisplayArea.Children.Clear();

        _partEditorControl = new PartEditorControl();
        _partEditorControl.LoadFromJsonFile(importFilePath);
        _partEditorControl.ReturnRequested += OnPartEditorReturn;

        MainDisplayArea.Children.Add(_partEditorControl);
        Logger.Instance.Info($"Part editor displayed for {importFilePath}");
    }

    /// <summary>
    /// Handle part editor Return event.
    /// </summary>
    private void OnPartEditorReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _partEditorControl = null;
        Logger.Instance.Info("Returned to main view from part editor");
    }

    /// <summary>
    /// Handle View Drawings button click.
    /// Dev: hardcoded to part.id=3490.
    /// </summary>
    private void ViewDrawingsButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("View Drawings button clicked");
        ShowDrawingViewerByPartId(3490);
    }

    /// <summary>
    /// Show drawing viewer and load the full tree rooted above the given part ID.
    /// </summary>
    private void ShowDrawingViewerByPartId(int partId)
    {
        MainDisplayArea.Children.Clear();

        _drawingViewerControl = new DrawingViewerControl();
        _drawingViewerControl.LoadFromPartId(partId);
        _drawingViewerControl.ReturnRequested += OnDrawingViewerReturn;

        MainDisplayArea.Children.Add(_drawingViewerControl);
        Logger.Instance.Info($"Drawing viewer displayed for partId={partId}");
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
