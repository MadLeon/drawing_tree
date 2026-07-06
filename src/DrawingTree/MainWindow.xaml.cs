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
        _allPosControl.NavigateToPoRequested  += OnAllPosNavigateToPo;
        _allPosControl.ImportDrawingRequested += OnAllPosImportDrawing;
        _allPosControl.HistoryRequested       += OnAllPosHistoryRequested;
        _allPosControl.NavigateToTreeRequested += OnAllPosNavigateToTree;
        _allPosControl.NavigateToPartRequested += OnAllPosNavigateToPart;
        _allPosControl.EditPartsRequested     += OnAllPosEditParts;

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

    // ── Edit Parts workflow (entered from All POs "Edit Parts" menu) ─────────

    /// <summary>
    /// Opens a file dialog to select an *_import.json file, then shows the PartEditorControl.
    /// Called when the user chooses "Edit Parts" from a PO three-dot menu in All POs.
    /// </summary>
    private void OnAllPosEditParts(object? sender, EventArgs e)
    {
        string dir = ImportsDir;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title            = "Select Import JSON File",
            Filter           = "Import JSON files|*_import.json|All JSON files|*.json",
            InitialDirectory = dir
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        MainDisplayArea.Children.Clear();
        _partEditorControl = new PartEditorControl();
        _partEditorControl.LoadFromJsonFile(dialog.FileName);
        _partEditorControl.ReturnRequested  += OnPartEditorReturnToAllPos;
        _partEditorControl.SaveAllCompleted += OnPartEditorSaveAllCompleted;
        MainDisplayArea.Children.Add(_partEditorControl);
        Logger.Instance.Info($"Part editor displayed from AllPos Edit Parts for {dialog.FileName}");
    }

    // ── Import Drawing workflow (entered from All POs "Input Data") ───────────

    /// <summary>
    /// Opens folder dialog, scans PDFs, and shows DrawingEditorControl with PO pre-filled.
    /// Called when the user chooses "Input Data" from the All POs PO header menu.
    /// </summary>
    private void OnAllPosImportDrawing(object? sender, string poNumber)
    {
        Logger.Instance.Info($"Import Drawing requested for PO={poNumber}");

        using var folderDialog = new FolderBrowserDialog { ShowNewFolderButton = false };
        if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var drawings = _drawingExtractor.ScanFolder(folderDialog.SelectedPath);
        if (drawings.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No PDF drawings found in the selected folder.",
                "No Drawings Found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainDisplayArea.Children.Clear();
        _drawingEditorControl = new DrawingEditorControl
        {
            PrefilledPoNumber = poNumber
        };
        _drawingEditorControl.LoadDrawings(drawings);
        _drawingEditorControl.ReturnRequested  += OnDrawingEditorReturn;
        _drawingEditorControl.ImportCompleted  += OnDrawingEditorImportCompleted;

        MainDisplayArea.Children.Add(_drawingEditorControl);
        Logger.Instance.Info($"Drawing editor displayed for PO={poNumber}");
    }

    /// <summary>
    /// Fired after DrawingEditorControl exports JSON.
    /// Auto-navigates to PartEditorControl.
    /// </summary>
    private void OnDrawingEditorImportCompleted(object? sender, string jsonFilePath)
    {
        MainDisplayArea.Children.Clear();
        _drawingEditorControl = null;

        _partEditorControl = new PartEditorControl();
        _partEditorControl.LoadFromJsonFile(jsonFilePath);
        _partEditorControl.ReturnRequested   += OnPartEditorReturnToAllPos;
        _partEditorControl.SaveAllCompleted  += OnPartEditorSaveAllCompleted;

        MainDisplayArea.Children.Add(_partEditorControl);
        Logger.Instance.Info($"Part editor auto-displayed after import for {jsonFilePath}");
    }

    /// <summary>
    /// Fired after PartEditorControl Save All.
    /// Auto-navigates to TreeBuilderControl.
    /// </summary>
    private void OnPartEditorSaveAllCompleted(object? sender, string importFilePath)
    {
        MainDisplayArea.Children.Clear();
        _partEditorControl = null;

        _treeBuilderControl = new TreeBuilderControl();
        _treeBuilderControl.LoadFromJsonFile(importFilePath);
        _treeBuilderControl.ReturnRequested += OnTreeBuilderReturnToAllPos;

        MainDisplayArea.Children.Add(_treeBuilderControl);
        Logger.Instance.Info($"Tree builder auto-displayed after part editor save for {importFilePath}");
    }

    private void OnDrawingEditorReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingEditorControl = null;
        if (_allPosControl != null)
            MainDisplayArea.Children.Add(_allPosControl);
        else
            Logger.Instance.Info("Returned to main view from drawing editor");
    }

    private void OnPartEditorReturnToAllPos(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _partEditorControl = null;
        if (_allPosControl != null)
            MainDisplayArea.Children.Add(_allPosControl);
        Logger.Instance.Info("Returned to All POs from part editor");
    }

    private void OnTreeBuilderReturnToAllPos(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _treeBuilderControl = null;
        if (_allPosControl != null)
            MainDisplayArea.Children.Add(_allPosControl);
        Logger.Instance.Info("Returned to All POs from tree builder");
    }

    private void OnTreeBuilderReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _treeBuilderControl = null;
        Logger.Instance.Info("Returned to main view from tree builder");
    }

    private void OnPartEditorReturn(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _partEditorControl = null;
        Logger.Instance.Info("Returned to main view from part editor");
    }

    // ── History view ──────────────────────────────────────────────────────────

    private void OnAllPosHistoryRequested(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();

        var historyControl = new AllPosControl { ShowHistoryOnly = true };
        historyControl.NavigateToPoRequested += OnAllPosNavigateToPo;

        MainDisplayArea.Children.Add(historyControl);
        Logger.Instance.Info("PO History control displayed");
    }

    // ── Part / Tree navigation from simple view ──────────────────────────────

    private void OnAllPosNavigateToPart(object? sender, (int PartId, int OrderItemId) args)
    {
        MainDisplayArea.Children.Clear();

        _partDetailControl = new PartDetailControl();
        _partDetailControl.LoadPart(args.PartId, args.OrderItemId);
        _partDetailControl.BackRequested     += OnPartDetailBackToAllPos;
        _partDetailControl.ViewTreeRequested += OnPartDetailViewTree;

        MainDisplayArea.Children.Add(_partDetailControl);
        Logger.Instance.Info($"Part detail displayed for partId={args.PartId}, orderItemId={args.OrderItemId} (from All POs)");
    }

    private void OnPartDetailBackToAllPos(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _partDetailControl = null;
        if (_allPosControl != null)
            MainDisplayArea.Children.Add(_allPosControl);
        Logger.Instance.Info("Returned to All POs from part detail");
    }

    private void OnAllPosNavigateToTree(object? sender, string poNumber)
    {
        MainDisplayArea.Children.Clear();

        _drawingViewerControl = new DrawingViewerControl();
        _drawingViewerControl.ShowBackButton = true;
        _drawingViewerControl.LoadFromDatabase(poNumber);
        _drawingViewerControl.ReturnRequested += OnDrawingViewerReturn;
        _drawingViewerControl.BackRequested   += OnViewerBackToAllPos;

        MainDisplayArea.Children.Add(_drawingViewerControl);
        Logger.Instance.Info($"Drawing viewer displayed for PO={poNumber} (from All POs tree button)");
    }

    private void OnViewerBackToAllPos(object? sender, EventArgs e)
    {
        MainDisplayArea.Children.Clear();
        _drawingViewerControl = null;
        if (_allPosControl != null)
            MainDisplayArea.Children.Add(_allPosControl);
        Logger.Instance.Info("Returned to All POs from drawing viewer");
    }

    // ── Settings menu ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the settings ContextMenu when the gear button is clicked.
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            btn.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(
                    new System.Windows.Point(targetSize.Width - popupSize.Width, targetSize.Height),
                    System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
            btn.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// Opens config.txt from the application directory in the default text editor.
    /// </summary>
    private void ConfigMenu_Click(object sender, RoutedEventArgs e)
    {
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, "# Application Configuration\n");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(configPath) { UseShellExecute = true });
        Logger.Instance.Info($"Opened config.txt at {configPath}");
    }

    /// <summary>
    /// Opens the Run Script dialog.
    /// </summary>
    private void RunScriptMenu_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.RunScriptDialog { Owner = this };
        dlg.ShowDialog();
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
