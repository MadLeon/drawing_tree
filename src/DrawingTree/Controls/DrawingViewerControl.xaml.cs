/// <summary>
/// DrawingViewerControl.xaml.cs
/// Read-only viewer for a drawing tree with integrated PDF display.
/// </summary>
/// <remarks>
/// Usage:
/// - Call LoadFromJsonFile(path) to populate from a *_tree.json file
/// - Call LoadFromTreeNodes(nodes) to populate from an external data source (e.g. database)
/// </remarks>

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Models;

using UserControl    = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Button         = System.Windows.Controls.Button;
using Point          = System.Windows.Point;
using MessageBox     = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using Image          = System.Windows.Controls.Image;

namespace DrawingTree.Controls;

public partial class DrawingViewerControl : UserControl
{
    // Render PDF pages once at this scale factor, then zoom via LayoutTransform (no re-render on zoom)
    private const double BaseRenderScale = 1.5;

    private readonly ObservableCollection<DrawingNode> _rootNodes = new();
    private readonly PoRepository _poRepository = new();
    private readonly DrawingRepository _drawingRepository = new();
    private DrawingNode? _selectedNode = null;
    private bool _infoUpdating = false;

    // Zoom state (visual scale via LayoutTransform — no re-render on change)
    private double _pdfZoom = 1.0;
    private const double ZoomStep = 0.15;
    private const double ZoomMin  = 0.2;
    private const double ZoomMax  = 5.0;

    // Pan state
    private bool  _isPanning  = false;
    private Point _panStart;
    private double _panScrollH;
    private double _panScrollV;

    public event EventHandler? ReturnRequested;
    public event EventHandler? BackRequested;

    /// <summary>
    /// Shows or hides the Back button. Set to true when navigated from the search screen.
    /// </summary>
    public bool ShowBackButton
    {
        get => BackButton.Visibility == Visibility.Visible;
        set => BackButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public DrawingViewerControl()
    {
        InitializeComponent();
        ViewerTreeView.ItemsSource = _rootNodes;
        _rootNodes.CollectionChanged += (_, e) =>
        {
            DrawingNode.UpdateLastChildFlags(_rootNodes);
            if (e.NewItems != null)
                foreach (DrawingNode n in e.NewItems) n.IsRootNode = true;
            if (e.OldItems != null)
                foreach (DrawingNode n in e.OldItems) n.IsRootNode = false;
        };
    }

    // ── Public data loading API ───────────────────────────────────────────

    /// <summary>
    /// Loads the drawing tree from a *_tree.json file.
    /// </summary>
    public void LoadFromJsonFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string po = root.TryGetProperty("PurchaseOrder", out var poEl)
                ? poEl.GetString() ?? string.Empty
                : Path.GetFileNameWithoutExtension(filePath);

            ViewerTitleLabel.Text = po;

            var nodes = new List<DrawingNode>();
            if (root.TryGetProperty("Tree", out var treeEl))
                foreach (var nodeEl in treeEl.EnumerateArray())
                    nodes.Add(ParseNode(nodeEl));

            LoadFromTreeNodes(nodes);
            Logger.Instance.Info($"DrawingViewer loaded {nodes.Count} root node(s) from {filePath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load tree: {ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"DrawingViewer failed to load {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates the viewer from a pre-built node list.
    /// Future database-driven loading calls this method.
    /// </summary>
    public void LoadFromTreeNodes(IList<DrawingNode> nodes)
    {
        _rootNodes.Clear();
        ClearInfoPanel();
        PdfPagesPanel.Children.Clear();

        foreach (var node in nodes)
        {
            ExpandAllNodes(node);
            _rootNodes.Add(node);
        }
    }

    /// <summary>
    /// Loads the drawing tree by walking up from the given part ID to the root,
    /// then loading the full subtree. Initially selects and highlights the given part.
    /// Fires async internally; caller does not need to await.
    /// </summary>
    /// <param name="partId">The part.id of the drawing to highlight on load</param>
    public void LoadFromPartId(int partId)
    {
        _ = LoadFromPartIdAsync(partId);
    }

    private async Task LoadFromPartIdAsync(int partId)
    {
        try
        {
            int rootPartId = await Task.Run(() => _poRepository.GetRootPartId(partId));
            var rootInfo   = await Task.Run(() => _drawingRepository.GetDrawingInfo(rootPartId));
            var children   = await Task.Run(() => _poRepository.GetPartTree(rootPartId));

            var drawingInfo = new DrawingInfo
            {
                PartId        = rootPartId,
                DrawingNumber = rootInfo?.DrawingNumber ?? rootPartId.ToString(),
                Revision      = rootInfo?.Revision      ?? string.Empty,
                Description   = rootInfo?.Description   ?? string.Empty,
                IsAssembly    = rootInfo?.IsAssembly     ?? false,
                PdfPath       = rootInfo?.PdfPath        ?? string.Empty
            };
            var rootNode = new DrawingNode(drawingInfo);
            foreach (var child in children)
                rootNode.Children.Add(child);

            ViewerTitleLabel.Text = drawingInfo.DrawingNumber;
            LoadFromTreeNodes(new List<DrawingNode> { rootNode });

            var target = FindNodeByPartId(_rootNodes, partId);
            if (target != null) SelectNode(target);

            Logger.Instance.Info($"DrawingViewer loaded tree for partId={partId}, root partId={rootPartId}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load drawing from database:\n{ex.Message}", "Database Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"DrawingViewer LoadFromPartId failed for partId={partId}: {ex.Message}");
        }
    }

    private static DrawingNode? FindNodeByPartId(IEnumerable<DrawingNode> nodes, int partId)
    {
        foreach (var node in nodes)
        {
            if (node.Drawing.PartId == partId) return node;
            var found = FindNodeByPartId(node.Children, partId);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Loads the drawing tree from the database for the given PO number.
    /// Fires async internally; caller does not need to await.
    /// </summary>
    public void LoadFromDatabase(string poName)
    {
        ViewerTitleLabel.Text = poName;
        _ = LoadFromDatabaseAsync(poName);
    }

    private async Task LoadFromDatabaseAsync(string poName)
    {
        try
        {
            var groups = await Task.Run(() => _poRepository.GetGroupsForPo(poName));

            var allRoots = new List<DrawingNode>();
            foreach (var group in groups)
            {
                var dbInfo   = await Task.Run(() => _drawingRepository.GetDrawingInfo(group.DrawingNumber));
                var children = await Task.Run(() => _poRepository.GetPartTree(group.PartId));

                var rootInfo = new DrawingInfo
                {
                    PartId        = group.PartId,
                    DrawingNumber = group.DrawingNumber,
                    Revision      = dbInfo?.Revision      ?? string.Empty,
                    Description   = dbInfo?.Description   ?? string.Empty,
                    IsAssembly    = dbInfo?.IsAssembly     ?? false,
                    PdfPath       = dbInfo?.PdfPath        ?? string.Empty
                };
                var rootNode = new DrawingNode(rootInfo)
                {
                    JobHeader  = "Job Number: "  + string.Join(" & ", group.JobNumbers),
                    LineHeader = "Line Number: " + string.Join(" & ", group.LineNumbers)
                };
                foreach (var child in children)
                    rootNode.Children.Add(child);

                allRoots.Add(rootNode);
            }

            LoadFromTreeNodes(allRoots);
            Logger.Instance.Info($"DrawingViewer loaded {allRoots.Count} root node(s) from DB for PO: {poName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load tree from database:\n{ex.Message}", "Database Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"DrawingViewer DB load failed for '{poName}': {ex.Message}");
        }
    }

    // ── JSON parsing ──────────────────────────────────────────────────────

    private static DrawingNode ParseNode(JsonElement el)
    {
        var info = new DrawingInfo
        {
            DrawingNumber      = Str(el, "DrawingNumber"),
            PdfPath            = Str(el, "PdfPath"),
            Revision           = Str(el, "Revision"),
            Description        = Str(el, "Description"),
            QuantityInAssembly = Str(el, "QuantityInAssembly"),
            IsAssembly         = el.TryGetProperty("IsAssembly", out var ia) && ia.GetBoolean(),
        };
        var node = new DrawingNode(info);
        if (el.TryGetProperty("Children", out var children))
            foreach (var child in children.EnumerateArray())
                node.Children.Add(ParseNode(child));
        return node;
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    private static void ExpandAllNodes(DrawingNode node)
    {
        node.IsExpanded = true;
        foreach (var child in node.Children)
            ExpandAllNodes(child);
    }

    // ── Tree: expand/collapse and node selection ──────────────────────────

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is DrawingNode node)
            node.IsExpanded = !node.IsExpanded;
        e.Handled = true;
    }

    private void ViewerNodeBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsButtonInPath(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement el && el.DataContext is DrawingNode node)
            SelectNode(node);
    }

    private void ViewerOpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is DrawingNode node)
            OpenPdfExternal(node.Drawing.PdfPath);
        e.Handled = true;
    }

    private void ViewerTreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewerTreeScrollViewer.ScrollToVerticalOffset(ViewerTreeScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void SelectNode(DrawingNode node)
    {
        if (_selectedNode != null) _selectedNode.IsSelected = false;
        _selectedNode = node;
        node.IsSelected = true;
        ShowInfo(node.Drawing);
        _ = LoadPdfAsync(node.Drawing.PdfPath);
    }

    // ── Info panel ────────────────────────────────────────────────────────

    private void ShowInfo(DrawingInfo info)
    {
        _infoUpdating            = true;
        InfoDrawingNumber.Text   = info.DrawingNumber;
        InfoRevision.Text        = info.Revision;
        InfoDescription.Text     = info.Description;
        InfoQuantity.Text        = info.QuantityInAssembly;
        InfoIsAssembly.IsChecked = info.IsAssembly;
        InfoFilePath.Text        = info.PdfPath;
        InfoSaveError.Visibility = Visibility.Collapsed;
        InfoPanel.IsEnabled      = true;
        _infoUpdating            = false;
    }

    private void ClearInfoPanel()
    {
        if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }
        _infoUpdating            = true;
        InfoDrawingNumber.Text   = string.Empty;
        InfoRevision.Text        = string.Empty;
        InfoDescription.Text     = string.Empty;
        InfoQuantity.Text        = string.Empty;
        InfoIsAssembly.IsChecked = false;
        InfoFilePath.Text        = string.Empty;
        InfoSaveError.Visibility = Visibility.Collapsed;
        InfoPanel.IsEnabled      = false;
        _infoUpdating            = false;
    }

    // ── Info panel: edit handlers ─────────────────────────────────────────

    private void InfoRevision_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedNode == null) return;
        _selectedNode.Drawing.Revision = InfoRevision.Text;
    }

    private void InfoDescription_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedNode == null) return;
        _selectedNode.Drawing.Description = InfoDescription.Text;
    }

    private void InfoQuantity_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedNode == null) return;
        _selectedNode.Drawing.QuantityInAssembly = InfoQuantity.Text;
    }

    private void InfoIsAssembly_Changed(object sender, RoutedEventArgs e)
    {
        if (_infoUpdating || _selectedNode == null) return;
        _selectedNode.Drawing.IsAssembly = InfoIsAssembly.IsChecked == true;
    }

    private void InfoFilePath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedNode == null) return;
        _selectedNode.Drawing.PdfPath = InfoFilePath.Text;
    }

    private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF files|*.pdf" };
        if (dialog.ShowDialog() == true)
            InfoFilePath.Text = dialog.FileName;
    }

    private void InfoSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode == null) return;
        InfoSaveError.Visibility = Visibility.Collapsed;

        var info = _selectedNode.Drawing;
        if (info.PartId == null)
        {
            InfoSaveError.Text = "This drawing has not been linked to the database yet.";
            InfoSaveError.Visibility = Visibility.Visible;
            return;
        }

        int partId = info.PartId.Value;

        bool partOk = _drawingRepository.UpdatePart(
            partId,
            InfoRevision.Text.Trim(),
            InfoDescription.Text.Trim(),
            InfoIsAssembly.IsChecked == true);

        if (!partOk)
        {
            InfoSaveError.Text = "Failed to save drawing info. Check logs for details.";
            InfoSaveError.Visibility = Visibility.Visible;
            return;
        }

        string filePath = InfoFilePath.Text.Trim();
        if (!string.IsNullOrEmpty(filePath))
        {
            string fileName = Path.GetFileName(filePath);
            bool fileOk = _drawingRepository.UpsertDrawingFile(
                partId, fileName, filePath, InfoRevision.Text.Trim());

            if (!fileOk)
            {
                InfoSaveError.Text = "Part info saved, but failed to update file path. Check logs.";
                InfoSaveError.Visibility = Visibility.Visible;
                return;
            }
        }

        string quantity = InfoQuantity.Text.Trim();
        if (!string.IsNullOrEmpty(quantity))
            _drawingRepository.UpdatePartTreeQuantity(partId, quantity);

        // Sync in-memory model
        info.Revision    = InfoRevision.Text.Trim();
        info.Description = InfoDescription.Text.Trim();
        info.IsAssembly  = InfoIsAssembly.IsChecked == true;
        info.PdfPath     = filePath;

        Logger.Instance.Info($"Info panel saved: {info.DrawingNumber} (partId={partId})");
        Snackbar.Show($"Saved: {info.DrawingNumber}");
    }

    // ── PDF rendering ─────────────────────────────────────────────────────

    /// <summary>
    /// Renders all PDF pages at BaseRenderScale and adds them to PdfPagesPanel.
    /// Zoom is handled entirely by PdfZoomTransform (no re-render on zoom).
    /// </summary>
    private async Task LoadPdfAsync(string path)
    {
        PdfPagesPanel.Children.Clear();

        // Reset zoom when switching documents
        _pdfZoom = 1.0;
        PdfZoomTransform.ScaleX = 1.0;
        PdfZoomTransform.ScaleY = 1.0;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Logger.Instance.Warning($"PDF not found: {path}");
            return;
        }

        try
        {
            var file    = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var pdfDoc  = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

            double firstPageWidth = 0, firstPageHeight = 0;

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                if (i == 0)
                {
                    firstPageWidth  = page.Size.Width;
                    firstPageHeight = page.Size.Height;
                }

                var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(ms, new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth  = (uint)(page.Size.Width  * BaseRenderScale),
                    DestinationHeight = (uint)(page.Size.Height * BaseRenderScale)
                });

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms.AsStream();
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                PdfPagesPanel.Children.Add(new Image
                {
                    Source  = bmp,
                    Margin  = new Thickness(0, 0, 0, 8),
                    Stretch = Stretch.None
                });
            }

            if (firstPageWidth > 0 && firstPageHeight > 0)
                FitPdfToViewport(firstPageWidth, firstPageHeight);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to render PDF {path}: {ex.Message}");
            MessageBox.Show($"Failed to render PDF:\n{ex.Message}", "PDF Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Sets the initial zoom so the page's larger dimension (width for landscape,
    /// height for portrait) fills 100% of the corresponding scroll viewport dimension.
    /// </summary>
    /// <param name="pageWidth">PDF page width in points (unscaled)</param>
    /// <param name="pageHeight">PDF page height in points (unscaled)</param>
    private void FitPdfToViewport(double pageWidth, double pageHeight)
    {
        double viewportWidth  = PdfScrollViewer.ActualWidth  - PdfPagesPanel.Margin.Left - PdfPagesPanel.Margin.Right;
        double viewportHeight = PdfScrollViewer.ActualHeight - PdfPagesPanel.Margin.Top  - PdfPagesPanel.Margin.Bottom;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        double renderedWidth  = pageWidth  * BaseRenderScale;
        double renderedHeight = pageHeight * BaseRenderScale;

        double scale = pageWidth >= pageHeight
            ? viewportWidth  / renderedWidth
            : viewportHeight / renderedHeight;

        _pdfZoom = Math.Clamp(scale, ZoomMin, ZoomMax);
        PdfZoomTransform.ScaleX = _pdfZoom;
        PdfZoomTransform.ScaleY = _pdfZoom;
    }

    // ── PDF zoom: instant via LayoutTransform, no re-render ───────────────

    private void PdfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double newZoom = Math.Clamp(_pdfZoom + (e.Delta > 0 ? ZoomStep : -ZoomStep), ZoomMin, ZoomMax);
        _pdfZoom = newZoom;
        PdfZoomTransform.ScaleX = _pdfZoom;
        PdfZoomTransform.ScaleY = _pdfZoom;
        e.Handled = true;
    }

    // ── PDF pan: Preview events so child images don't block ──────────────

    private void PdfScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsScrollBarInPath(e.OriginalSource as DependencyObject)) return;
        _isPanning  = true;
        _panStart   = e.GetPosition(PdfScrollViewer);
        _panScrollH = PdfScrollViewer.HorizontalOffset;
        _panScrollV = PdfScrollViewer.VerticalOffset;
    }

    private void PdfScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
    }

    private void PdfScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(PdfScrollViewer);
        PdfScrollViewer.ScrollToHorizontalOffset(_panScrollH + (_panStart.X - current.X));
        PdfScrollViewer.ScrollToVerticalOffset(  _panScrollV + (_panStart.Y - current.Y));
        e.Handled = true;
    }

    // ── Toolbar ───────────────────────────────────────────────────────────

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("DrawingViewer: back to search");
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("DrawingViewer: returning to home");
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode == null)
        {
            MessageBox.Show("Please select a drawing first.", "No Drawing Selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenPdfExternal(_selectedNode.Drawing.PdfPath);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void OpenPdfExternal(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show("PDF file not found.", "File Not Found",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open PDF: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsButtonInPath(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static bool IsScrollBarInPath(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is System.Windows.Controls.Primitives.ScrollBar) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
}
