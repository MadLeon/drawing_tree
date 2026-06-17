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
    private SearchControl? _searchControl;
    private AllPosControl? _allPosControl;
    private PoDetailControl? _poDetailControl;
    private PartDetailControl? _partDetailControl;
    private ManufacturingScheduleControl? _manufacturingScheduleControl;

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
        ShowSearch();
    }

    /// <summary>
    /// Show the search control in the main display area.
    /// </summary>
    private void ShowSearch()
    {
        MainDisplayArea.Children.Clear();

        _searchControl = new SearchControl();
        _searchControl.NavigateToPartRequested += OnSearchNavigateToPart;
        _searchControl.NavigateToPoRequested   += OnSearchNavigateToPo;

        MainDisplayArea.Children.Add(_searchControl);
        Logger.Instance.Info("Search control displayed");
    }

    /// <summary>
    /// Handle navigation request from search results.
    /// Keeps the search control alive for back navigation.
    /// </summary>
    private void OnSearchNavigateToPart(object? sender, int partId)
    {
        MainDisplayArea.Children.Clear();

        _drawingViewerControl = new DrawingViewerControl();
        _drawingViewerControl.ShowBackButton = true;
        _drawingViewerControl.LoadFromPartId(partId);
        _drawingViewerControl.ReturnRequested += OnDrawingViewerReturn;
        _drawingViewerControl.BackRequested += OnViewerBackToSearch;

        MainDisplayArea.Children.Add(_drawingViewerControl);
        Logger.Instance.Info($"Drawing viewer displayed for partId={partId} (from search)");
    }

    /// <summary>
    /// Handle navigation to PO detail from search results (PO or job match).
    /// Keeps the search control alive for back navigation.
    /// </summary>
    private void OnSearchNavigateToPo(object? sender, int poId)
    {
        MainDisplayArea.Children.Clear();

        _poDetailControl = new PoDetailControl();
        _poDetailControl.LoadPo(poId);
        _poDetailControl.BackRequested      += OnPoDetailBackToSearch;
        _poDetailControl.ViewTreeRequested  += OnPoDetailViewTree;
        _poDetailControl.OpenPartRequested  += OnPoDetailOpenPart;

        MainDisplayArea.Children.Add(_poDetailControl);
        Logger.Instance.Info($"PO detail displayed for poId={poId} (from search)");
    }

    /// <summary>
    /// Handle Back event from PO detail when entered from search. Restores search control.
    /// </summary>
    private void OnPoDetailBackToSearch(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _poDetailControl = null;
        if (_searchControl != null)
            MainDisplayArea.Children.Add(_searchControl);
        Logger.Instance.Info("Returned to search from PO detail");
    }

    /// <summary>
    /// Handle drawing viewer Back event. Restores the search control with its previous state.
    /// </summary>
    private void OnViewerBackToSearch(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingViewerControl = null;
        if (_searchControl != null)
            MainDisplayArea.Children.Add(_searchControl);
        Logger.Instance.Info("Returned to search from drawing viewer");
    }

    /// <summary>
    /// Handle Manufacturing Schedule button click event
    /// </summary>
    private void ManufacturingScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Manufacturing Schedule button clicked");
        MainDisplayArea.Children.Clear();

        _manufacturingScheduleControl = new ManufacturingScheduleControl();
        _manufacturingScheduleControl.BackRequested += (_, _) =>
        {
            MainDisplayArea.Children.Clear();
            _manufacturingScheduleControl = null;
            Logger.Instance.Info("Returned to main view from Manufacturing Schedule");
        };
        _manufacturingScheduleControl.OpenPartRequested += OnScheduleOpenPart;

        MainDisplayArea.Children.Add(_manufacturingScheduleControl);
        Logger.Instance.Info("Manufacturing Schedule control displayed");
    }

    /// <summary>
    /// Handle "open part" request from the Manufacturing Schedule.
    /// Shows the Part detail page; back returns to the schedule.
    /// </summary>
    private void OnScheduleOpenPart(object? sender, (int PartId, int OrderItemId) args)
    {
        MainDisplayArea.Children.Clear();

        _partDetailControl = new PartDetailControl();
        _partDetailControl.LoadPart(args.PartId, args.OrderItemId);
        _partDetailControl.BackRequested    += OnPartDetailBackToSchedule;
        _partDetailControl.ViewTreeRequested += OnPartDetailViewTree;

        MainDisplayArea.Children.Add(_partDetailControl);
        Logger.Instance.Info($"Part detail displayed for partId={args.PartId}, orderItemId={args.OrderItemId} (from schedule)");
    }

    /// <summary>
    /// Handle Back event from Part detail when entered from the Manufacturing Schedule.
    /// </summary>
    private void OnPartDetailBackToSchedule(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _partDetailControl = null;
        if (_manufacturingScheduleControl != null)
            MainDisplayArea.Children.Add(_manufacturingScheduleControl);
        Logger.Instance.Info("Returned to Manufacturing Schedule from part detail");
    }

    /// <summary>
    /// Handle All POs button click event
    /// </summary>
    private void AllPosButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("All POs button clicked");
        ShowAllPos();
    }

    /// <summary>
    /// Show the All POs control in the main display area.
    /// </summary>
    private void ShowAllPos()
    {
        MainDisplayArea.Children.Clear();

        _allPosControl = new AllPosControl();
        _allPosControl.NavigateToPoRequested += OnAllPosNavigateToPo;

        MainDisplayArea.Children.Add(_allPosControl);
        Logger.Instance.Info("All POs control displayed");
    }

    /// <summary>
    /// Handle navigation request from the All POs list. Shows the single-PO detail page.
    /// Keeps the All POs control alive for back navigation.
    /// </summary>
    private void OnAllPosNavigateToPo(object? sender, int poId)
    {
        MainDisplayArea.Children.Clear();

        _poDetailControl = new PoDetailControl();
        _poDetailControl.LoadPo(poId);
        _poDetailControl.BackRequested += OnPoDetailBackToAllPos;
        _poDetailControl.ViewTreeRequested += OnPoDetailViewTree;
        _poDetailControl.OpenPartRequested += OnPoDetailOpenPart;

        MainDisplayArea.Children.Add(_poDetailControl);
        Logger.Instance.Info($"PO detail control displayed for poId={poId}");
    }

    /// <summary>
    /// Handle Back event from the PO detail page. Restores the All POs list.
    /// </summary>
    private void OnPoDetailBackToAllPos(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _poDetailControl = null;
        if (_allPosControl != null)
            MainDisplayArea.Children.Add(_allPosControl);
        Logger.Instance.Info("Returned to All POs from PO detail");
    }

    /// <summary>
    /// Handle "View Tree" request from a PO detail order item row.
    /// Shows the full drawing viewer rooted at the given part, with Back returning to PO detail.
    /// </summary>
    private void OnPoDetailViewTree(object? sender, int partId)
    {
        MainDisplayArea.Children.Clear();

        _drawingViewerControl = new DrawingViewerControl();
        _drawingViewerControl.ShowBackButton = true;
        _drawingViewerControl.LoadFromPartId(partId);
        _drawingViewerControl.ReturnRequested += OnDrawingViewerReturn;
        _drawingViewerControl.BackRequested += OnViewerBackToPoDetail;

        MainDisplayArea.Children.Add(_drawingViewerControl);
        Logger.Instance.Info($"Drawing viewer displayed for partId={partId} (from PO detail)");
    }

    /// <summary>
    /// Handle drawing viewer Back event when entered from the PO detail page.
    /// </summary>
    private void OnViewerBackToPoDetail(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingViewerControl = null;
        if (_poDetailControl != null)
            MainDisplayArea.Children.Add(_poDetailControl);
        Logger.Instance.Info("Returned to PO detail from drawing viewer");
    }

    /// <summary>
    /// Handle "open part" request from the PO detail tree. Shows the Part detail page.
    /// Keeps the PO detail control alive for back navigation.
    /// </summary>
    private void OnPoDetailOpenPart(object? sender, (int PartId, int OrderItemId) args)
    {
        MainDisplayArea.Children.Clear();

        _partDetailControl = new PartDetailControl();
        _partDetailControl.LoadPart(args.PartId, args.OrderItemId);
        _partDetailControl.BackRequested += OnPartDetailBackToPoDetail;
        _partDetailControl.ViewTreeRequested += OnPartDetailViewTree;

        MainDisplayArea.Children.Add(_partDetailControl);
        Logger.Instance.Info($"Part detail control displayed for partId={args.PartId}, orderItemId={args.OrderItemId}");
    }

    /// <summary>
    /// Handle Back event from the Part detail page. Restores the PO detail page.
    /// </summary>
    private void OnPartDetailBackToPoDetail(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _partDetailControl = null;
        if (_poDetailControl != null)
            MainDisplayArea.Children.Add(_poDetailControl);
        Logger.Instance.Info("Returned to PO detail from part detail");
    }

    /// <summary>
    /// Handle "Tree View" request from the Part detail page.
    /// Shows the full drawing viewer rooted at the given part, with Back returning to Part detail.
    /// </summary>
    private void OnPartDetailViewTree(object? sender, int partId)
    {
        MainDisplayArea.Children.Clear();

        _drawingViewerControl = new DrawingViewerControl();
        _drawingViewerControl.ShowBackButton = true;
        _drawingViewerControl.LoadFromPartId(partId);
        _drawingViewerControl.ReturnRequested += OnDrawingViewerReturn;
        _drawingViewerControl.BackRequested += OnViewerBackToPartDetail;

        MainDisplayArea.Children.Add(_drawingViewerControl);
        Logger.Instance.Info($"Drawing viewer displayed for partId={partId} (from part detail)");
    }

    /// <summary>
    /// Handle drawing viewer Back event when entered from the Part detail page.
    /// </summary>
    private void OnViewerBackToPartDetail(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingViewerControl = null;
        if (_partDetailControl != null)
            MainDisplayArea.Children.Add(_partDetailControl);
        Logger.Instance.Info("Returned to part detail from drawing viewer");
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
    /// Handle drawing viewer Return event (Home button). Returns to blank main view.
    /// </summary>
    private void OnDrawingViewerReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingViewerControl = null;
        _searchControl = null;
        _allPosControl = null;
        _poDetailControl = null;
        _partDetailControl = null;
        Logger.Instance.Info("Returned to main view from drawing viewer");
    }
}
