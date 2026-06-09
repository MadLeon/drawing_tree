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
                var rootNode = new DrawingNode(rootInfo);
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
        InfoDrawingNumber.Text   = info.DrawingNumber;
        InfoRevision.Text        = info.Revision;
        InfoDescription.Text     = info.Description;
        InfoQuantity.Text        = info.QuantityInAssembly;
        InfoIsAssembly.IsChecked = info.IsAssembly;
        InfoFilePath.Text        = info.PdfPath;
        InfoPanel.IsEnabled      = true;
    }

    private void ClearInfoPanel()
    {
        if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }
        InfoDrawingNumber.Text   = string.Empty;
        InfoRevision.Text        = string.Empty;
        InfoDescription.Text     = string.Empty;
        InfoQuantity.Text        = string.Empty;
        InfoIsAssembly.IsChecked = false;
        InfoFilePath.Text        = string.Empty;
        InfoPanel.IsEnabled      = false;
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

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
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
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to render PDF {path}: {ex.Message}");
            MessageBox.Show($"Failed to render PDF:\n{ex.Message}", "PDF Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
}
