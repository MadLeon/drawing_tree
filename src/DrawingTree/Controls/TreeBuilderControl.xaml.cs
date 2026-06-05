using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DrawingTree.Logging;
using DrawingTree.Models;
using DrawingTree.Services;

using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Thickness = System.Windows.Thickness;
using CornerRadius = System.Windows.CornerRadius;
using Button = System.Windows.Controls.Button;

namespace DrawingTree.Controls;

/// <summary>
/// TreeBuilderControl.xaml.cs
/// User control for building drawing hierarchy relationships via drag and drop.
/// </summary>
public partial class TreeBuilderControl : UserControl
{
    private const string DragFormatInfo = "DrawingTree.DrawingInfo";
    private const string DragFormatNode = "DrawingTree.DrawingNode";

    private readonly ObservableCollection<DrawingNode> _rootNodes = new();
    private ObservableCollection<DrawingInfo> _leftDrawings = new();
    private readonly PoTreeService _poTreeService = new();
    private string _poName = string.Empty;
    private bool _hasUnsavedChanges = false;

    private Point _leftPanelDragStart;
    private Point _treePanelDragStart;
    private bool _dragInProgress = false;
    private DrawingNode? _currentDropTarget = null;

    // Selected drawing for info panel
    private DrawingInfo? _selectedDrawing = null;
    private DrawingNode? _selectedNode = null;
    private bool _infoUpdating = false;

    // Drag preview popup
    private Popup? _dragPopup = null;

    public event EventHandler? ReturnRequested;

    public TreeBuilderControl()
    {
        InitializeComponent();
        DrawingTreeView.ItemsSource = _rootNodes;
        _rootNodes.CollectionChanged += (_, e) =>
        {
            DrawingNode.UpdateLastChildFlags(_rootNodes);
            if (e.NewItems != null)
                foreach (DrawingNode n in e.NewItems) n.IsRootNode = true;
            if (e.OldItems != null)
                foreach (DrawingNode n in e.OldItems) n.IsRootNode = false;
        };
    }

    /// <summary>
    /// Load drawings from a JSON import file and populate both panels
    /// </summary>
    public void LoadFromJsonFile(string filePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        _poName = baseName.EndsWith("_import", StringComparison.OrdinalIgnoreCase)
            ? baseName[..^"_import".Length]
            : baseName;

        PoLabel.Text = $"PO: {_poName}";

        try
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _leftDrawings.Clear();
            _rootNodes.Clear();

            if (root.TryGetProperty("Drawings", out var drawings))
            {
                foreach (var d in drawings.EnumerateArray())
                {
                    _leftDrawings.Add(new DrawingInfo
                    {
                        DrawingNumber = d.GetProperty("DrawingNumber").GetString() ?? string.Empty,
                        PdfPath = d.GetProperty("PdfPath").GetString() ?? string.Empty
                    });
                }
            }

            DrawingListPanel.ItemsSource = _leftDrawings;
            Logger.Instance.Info($"TreeBuilder loaded {_leftDrawings.Count} drawings from {filePath}");

            SetupRootNodesFromPo(_poName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load drawings: {ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"TreeBuilder failed to load {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries root assembly groups for the PO and adds them as pre-set root nodes.
    /// Matching drawings are removed from the left panel (they are fixed roots, not draggable items).
    /// </summary>
    /// <param name="poNumber">PO number used for the lookup</param>
    private void SetupRootNodesFromPo(string poNumber)
    {
        var groups = _poTreeService.GetGroupsForPo(poNumber);
        if (groups.Count == 0)
        {
            Logger.Instance.Info($"No root assembly groups found for PO: {poNumber}");
            return;
        }

        foreach (var group in groups)
        {
            // Use existing DrawingInfo from left panel if present; otherwise create a placeholder
            var existing = _leftDrawings.FirstOrDefault(
                d => string.Equals(d.DrawingNumber, group.DrawingNumber, StringComparison.OrdinalIgnoreCase));

            DrawingInfo drawingInfo = existing ?? new DrawingInfo { DrawingNumber = group.DrawingNumber };
            if (existing != null)
                _leftDrawings.Remove(existing);

            var node = new DrawingNode(drawingInfo)
            {
                JobHeader  = "Job Number: "  + string.Join(" & ", group.JobNumbers),
                LineHeader = "Line Number: " + string.Join(" & ", group.LineNumbers)
            };
            _rootNodes.Add(node);
        }

        Logger.Instance.Info($"Set up {groups.Count} root assembly node(s) for PO: {poNumber}");
    }

    // ── Info panel ────────────────────────────────────────────────────────

    private void SelectDrawing(DrawingInfo info, DrawingNode? sourceNode = null)
    {
        // Clear previous selection highlights
        if (_selectedNode != null)
        {
            _selectedNode.IsSelected = false;
            _selectedNode = null;
        }
        if (_selectedDrawing != null)
            _selectedDrawing.IsSelected = false;

        _selectedDrawing = info;
        info.IsSelected = true;

        // Apply new tree selection highlight
        if (sourceNode != null)
        {
            _selectedNode = sourceNode;
            sourceNode.IsSelected = true;
        }

        _infoUpdating = true;
        InfoDrawingNumber.Text = info.DrawingNumber;
        InfoRevision.Text = info.Revision;
        InfoDescription.Text = info.Description;
        InfoQuantity.Text = info.QuantityInAssembly;
        InfoIsAssembly.IsChecked = info.IsAssembly;
        InfoFilePath.Text = info.PdfPath;
        InfoPanel.IsEnabled = true;
        _infoUpdating = false;
    }

    private void ClearInfoPanel()
    {
        if (_selectedNode != null)
        {
            _selectedNode.IsSelected = false;
            _selectedNode = null;
        }
        if (_selectedDrawing != null)
        {
            _selectedDrawing.IsSelected = false;
            _selectedDrawing = null;
        }
        _infoUpdating = true;
        InfoDrawingNumber.Text = string.Empty;
        InfoRevision.Text = string.Empty;
        InfoDescription.Text = string.Empty;
        InfoQuantity.Text = string.Empty;
        InfoIsAssembly.IsChecked = false;
        InfoFilePath.Text = string.Empty;
        InfoPanel.IsEnabled = false;
        _infoUpdating = false;
    }

    private void InfoRevision_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedDrawing == null) return;
        _selectedDrawing.Revision = InfoRevision.Text;
        _hasUnsavedChanges = true;
    }

    private void InfoDescription_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedDrawing == null) return;
        _selectedDrawing.Description = InfoDescription.Text;
        _hasUnsavedChanges = true;
    }

    private void InfoQuantity_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedDrawing == null) return;
        _selectedDrawing.QuantityInAssembly = InfoQuantity.Text;
        _hasUnsavedChanges = true;
    }

    private void InfoIsAssembly_Changed(object sender, RoutedEventArgs e)
    {
        if (_infoUpdating || _selectedDrawing == null) return;
        _selectedDrawing.IsAssembly = InfoIsAssembly.IsChecked == true;
        _hasUnsavedChanges = true;
    }

    // ── Left panel: selection and drag source ─────────────────────────────

    private void LeftItemBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragInProgress) return;
        if (sender is FrameworkElement el && el.DataContext is DrawingInfo info)
            SelectDrawing(info, null);
    }

    private void LeftOpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is DrawingInfo info)
            OpenPdf(info.PdfPath);
        e.Handled = true;
    }

    private void LeftPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _leftPanelDragStart = e.GetPosition(DrawingListPanel);
    }

    private void LeftPanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragInProgress) return;

        Point pos = e.GetPosition(DrawingListPanel);
        if (Math.Abs(pos.X - _leftPanelDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _leftPanelDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((e.OriginalSource as FrameworkElement)?.DataContext is not DrawingInfo info) return;
        if (!_leftDrawings.Contains(info)) return;

        info.IsDragging = true;
        _dragInProgress = true;
        ShowDragPopup(info.DrawingNumber);
        try
        {
            DragDrop.DoDragDrop(DrawingListPanel, new DataObject(DragFormatInfo, info), DragDropEffects.Move);
        }
        finally
        {
            info.IsDragging = false;
            CloseDragPopup();
            _dragInProgress = false;
        }
    }

    // ── Tree view: selection and drag source ─────────────────────────────

    private void TreeNodeBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragInProgress) return;
        if (IsButtonInPath(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement el && el.DataContext is DrawingNode node)
            SelectDrawing(node.Drawing, node);
    }

    private void RemoveFromTree_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is DrawingNode node)
        {
            if (node.HasJobInfo) { e.Handled = true; return; }  // Guard: root assembly nodes cannot be removed
            var (parentCol, _) = FindParent(node);
            parentCol?.Remove(node);

            _leftDrawings.Add(node.Drawing);
            var sorted = _leftDrawings.OrderBy(d => d.DrawingNumber, StringComparer.OrdinalIgnoreCase).ToList();
            _leftDrawings.Clear();
            foreach (var d in sorted) _leftDrawings.Add(d);

            if (_selectedNode == node) ClearInfoPanel();

            _hasUnsavedChanges = true;
            Logger.Instance.Info($"Removed {node.Drawing.DrawingNumber} from tree");
        }
        e.Handled = true;
    }

    private void TreeOpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is DrawingNode node)
            OpenPdf(node.Drawing.PdfPath);
        e.Handled = true;
    }

    private void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treePanelDragStart = e.GetPosition(DrawingTreeView);
    }

    private void TreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragInProgress) return;

        Point pos = e.GetPosition(DrawingTreeView);
        if (Math.Abs(pos.X - _treePanelDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _treePanelDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = GetTreeViewItemAt(_treePanelDragStart);
        if (item?.DataContext is not DrawingNode node) return;
        if (node.HasJobInfo) return;  // Root assembly nodes are fixed; cannot be dragged

        node.IsDragging = true;
        _dragInProgress = true;
        ShowDragPopup(node.Drawing.DrawingNumber);
        try
        {
            DragDrop.DoDragDrop(DrawingTreeView, new DataObject(DragFormatNode, node), DragDropEffects.Move);
        }
        finally
        {
            node.IsDragging = false;
            CloseDragPopup();
            _dragInProgress = false;
            ClearDropTarget();
        }
    }

    // ── Tree view: drop target ────────────────────────────────────────────

    private void TreeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent(DragFormatInfo) && !e.Data.GetDataPresent(DragFormatNode))
        {
            e.Handled = true;
            return;
        }

        var targetNode = GetTreeViewItemAt(e.GetPosition(DrawingTreeView))?.DataContext as DrawingNode;

        // Root level (no target node) is reserved for DB-defined assembly nodes — block all drops
        if (targetNode == null)
        {
            ClearDropTarget();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DragFormatNode))
        {
            var sourceNode = e.Data.GetData(DragFormatNode) as DrawingNode;
            if (sourceNode == null || sourceNode == targetNode || IsAncestorOf(sourceNode, targetNode))
            {
                ClearDropTarget();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
        }

        if (_currentDropTarget != targetNode)
        {
            if (_currentDropTarget != null) _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = targetNode;
            _currentDropTarget.IsDropTarget = true;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TreeView_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropTarget();
    }

    private void TreeView_Drop(object sender, DragEventArgs e)
    {
        ClearDropTarget();

        var targetNode = GetTreeViewItemAt(e.GetPosition(DrawingTreeView))?.DataContext as DrawingNode;

        // Root level is reserved for DB-defined assembly nodes — reject all drops here
        if (targetNode == null)
        {
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DragFormatInfo))
        {
            if (e.Data.GetData(DragFormatInfo) is not DrawingInfo info) return;

            var newNode = new DrawingNode(info);
            targetNode.Children.Add(newNode);
            _leftDrawings.Remove(info);
            SortCollection(targetNode.Children);
            _hasUnsavedChanges = true;
            Logger.Instance.Info($"Added {info.DrawingNumber} under {targetNode.Drawing.DrawingNumber}");
        }
        else if (e.Data.GetDataPresent(DragFormatNode))
        {
            if (e.Data.GetData(DragFormatNode) is not DrawingNode sourceNode) return;
            if (sourceNode == targetNode || IsAncestorOf(sourceNode, targetNode)) return;

            var (parentCol, _) = FindParent(sourceNode);
            parentCol?.Remove(sourceNode);
            targetNode.Children.Add(sourceNode);
            SortCollection(targetNode.Children);
            _hasUnsavedChanges = true;
        }

        e.Handled = true;
    }

    // ── Drag popup position tracking ──────────────────────────────────────

    private void RootGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (_dragPopup == null) return;
        var screenPos = PointToScreen(e.GetPosition(this));
        _dragPopup.HorizontalOffset = screenPos.X + 14;
        _dragPopup.VerticalOffset = screenPos.Y + 10;
    }

    // ── Save ─────────────────────────────────────────────────────────────

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string fileName = $"{_poName.ToUpper()}_tree.json";
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

            var treeData = new
            {
                PurchaseOrder = _poName.ToUpper(),
                SaveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Tree = _rootNodes.Select(SerializeNode).ToList()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(fullPath, JsonSerializer.Serialize(treeData, options));
            _hasUnsavedChanges = false;

            MessageBox.Show($"Drawing tree saved to:\n{fullPath}", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Logger.Instance.Info($"Drawing tree saved to {fullPath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"TreeBuilder save failed: {ex.Message}");
        }
    }

    private object SerializeNode(DrawingNode node) => new
    {
        DrawingNumber = node.Drawing.DrawingNumber,
        PdfPath = node.Drawing.PdfPath,
        Revision = node.Drawing.Revision,
        Description = node.Drawing.Description,
        QuantityInAssembly = node.Drawing.QuantityInAssembly,
        IsAssembly = node.Drawing.IsAssembly,
        Children = node.Children.Select(SerializeNode).ToList()
    };

    // ── Return ───────────────────────────────────────────────────────────

    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Are you sure you want to return?",
                "Confirm Return",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;
        }

        Logger.Instance.Info("Returning from tree builder");
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void ShowDragPopup(string drawingNumber)
    {
        var content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 173, 216, 230)),
            BorderBrush = new SolidColorBrush(Colors.SteelBlue),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 5, 10, 5),
            Child = new TextBlock
            {
                Text = drawingNumber,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.DarkSlateGray)
            }
        };

        _dragPopup = new Popup
        {
            Child = content,
            IsOpen = false,
            Placement = PlacementMode.Absolute,
            AllowsTransparency = true,
            PlacementTarget = this
        };
        _dragPopup.IsOpen = true;
    }

    private void CloseDragPopup()
    {
        if (_dragPopup != null)
        {
            _dragPopup.IsOpen = false;
            _dragPopup = null;
        }
    }

    private TreeViewItem? GetTreeViewItemAt(Point point)
    {
        var hitResult = VisualTreeHelper.HitTest(DrawingTreeView, point);
        if (hitResult == null) return null;

        DependencyObject? current = hitResult.VisualHit;
        while (current != null && current is not TreeViewItem)
            current = VisualTreeHelper.GetParent(current);

        return current as TreeViewItem;
    }

    private (ObservableCollection<DrawingNode>? collection, DrawingNode? parent) FindParent(DrawingNode target)
    {
        if (_rootNodes.Contains(target)) return (_rootNodes, null);
        foreach (var node in _rootNodes)
        {
            var result = FindParentInChildren(node, target);
            if (result.collection != null) return result;
        }
        return (null, null);
    }

    private (ObservableCollection<DrawingNode>? collection, DrawingNode? parent) FindParentInChildren(
        DrawingNode parent, DrawingNode target)
    {
        if (parent.Children.Contains(target)) return (parent.Children, parent);
        foreach (var child in parent.Children)
        {
            var result = FindParentInChildren(child, target);
            if (result.collection != null) return result;
        }
        return (null, null);
    }

    private bool IsAncestorOf(DrawingNode potentialAncestor, DrawingNode? node)
    {
        if (node == null) return false;
        if (potentialAncestor == node) return true;
        return potentialAncestor.Children.Any(c => IsAncestorOf(c, node));
    }

    private void SortCollection(ObservableCollection<DrawingNode> collection)
    {
        var sorted = collection.OrderBy(n => n.Drawing.DrawingNumber, StringComparer.OrdinalIgnoreCase).ToList();
        collection.Clear();
        foreach (var node in sorted)
            collection.Add(node);
    }

    private void ClearDropTarget()
    {
        if (_currentDropTarget != null)
        {
            _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = null;
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

    private static void OpenPdf(string path)
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
}
