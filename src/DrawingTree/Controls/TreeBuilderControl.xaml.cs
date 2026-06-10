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
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Models;

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
    private const string DragFormatInfo     = "DrawingTree.DrawingInfo";
    private const string DragFormatInfoList = "DrawingTree.DrawingInfoList";
    private const string DragFormatNode     = "DrawingTree.DrawingNode";

    private readonly ObservableCollection<DrawingNode> _rootNodes = new();
    private ObservableCollection<DrawingInfo> _leftDrawings = new();
    private readonly PoRepository _poRepository = new();
    private readonly DrawingRepository _drawingRepository = new();
    private string _poName = string.Empty;
    private bool _hasUnsavedChanges = false;

    private Point _leftPanelDragStart;
    private Point _treePanelDragStart;
    private bool _dragInProgress = false;
    private DrawingNode? _currentDropTarget = null;

    // Selected drawing for info panel (single)
    private DrawingInfo? _selectedDrawing = null;
    private DrawingNode? _selectedNode = null;
    private bool _infoUpdating = false;

    // Left panel multi-select
    private readonly List<DrawingInfo> _selectedDrawings = new();
    private DrawingInfo? _anchorDrawing = null;

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

            _ = LoadFromDatabaseAsync(_poName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load drawings: {ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"TreeBuilder failed to load {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads PO groups and pre-existing tree structure from the database asynchronously.
    /// Enriches left panel drawings with DB metadata, pre-populates saved tree relationships,
    /// and warns about drawings present in DB tree but absent from the import list.
    /// </summary>
    private async Task LoadFromDatabaseAsync(string poName)
    {
        SetLoading(true);
        try
        {
            var groups = await Task.Run(() => _poRepository.GetGroupsForPo(poName));
            if (groups.Count == 0)
            {
                Logger.Instance.Info($"No root assembly groups found for PO: {poName}");
                return;
            }

            // Enrich left panel drawings with DB metadata
            var snapshot = _leftDrawings.ToList();
            var enriched = await Task.Run(() =>
                snapshot.Select(d => (d, _drawingRepository.GetDrawingInfo(d.DrawingNumber))).ToList());

            foreach (var (drawing, dbInfo) in enriched)
            {
                if (dbInfo == null) continue;
                drawing.PartId      = dbInfo.PartId;
                drawing.Revision    = dbInfo.Revision;
                drawing.Description = dbInfo.Description;
                drawing.IsAssembly  = dbInfo.IsAssembly;
                if (string.IsNullOrEmpty(drawing.PdfPath))
                    drawing.PdfPath = dbInfo.PdfPath;
            }

            // Set up each root node and pre-load its saved tree
            foreach (var group in groups)
            {
                var treeChildren = await Task.Run(() => _poRepository.GetPartTree(group.PartId));
                SetupRootNodeFromGroup(group, treeChildren);
            }

            Logger.Instance.Info($"Loaded {groups.Count} root node(s) from DB for PO: {poName}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"LoadFromDatabaseAsync failed for '{poName}': {ex.Message}");
            MessageBox.Show($"Failed to load data from database:\n{ex.Message}", "Database Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetLoading(false);
        }
    }

    /// <summary>
    /// Creates a root node for the given PO group and attaches any pre-existing DB children.
    /// </summary>
    private void SetupRootNodeFromGroup(PoTreeGroup group, List<DrawingNode> dbChildren)
    {
        var existing = _leftDrawings.FirstOrDefault(
            d => string.Equals(d.DrawingNumber, group.DrawingNumber, StringComparison.OrdinalIgnoreCase));

        DrawingInfo rootInfo = existing ?? new DrawingInfo { DrawingNumber = group.DrawingNumber };
        rootInfo.PartId = group.PartId;
        if (existing != null)
            _leftDrawings.Remove(existing);

        var rootNode = new DrawingNode(rootInfo)
        {
            JobHeader  = "Job Number: "  + string.Join(" & ", group.JobNumbers),
            LineHeader = "Line Number: " + string.Join(" & ", group.LineNumbers)
        };

        if (dbChildren.Count > 0)
            AttachDbChildren(rootNode, dbChildren);

        _rootNodes.Add(rootNode);
    }

    /// <summary>
    /// Recursively attaches DB tree nodes to the parent, using left panel DrawingInfo where available.
    /// Skips and warns for any DB node whose drawing number is absent from the import list.
    /// </summary>
    private void AttachDbChildren(DrawingNode parent, IEnumerable<DrawingNode> dbChildren)
    {
        foreach (var dbChild in dbChildren)
        {
            var leftMatch = _leftDrawings.FirstOrDefault(
                d => string.Equals(d.DrawingNumber, dbChild.Drawing.DrawingNumber,
                    StringComparison.OrdinalIgnoreCase));

            if (leftMatch == null)
            {
                Logger.Instance.Warning(
                    $"Drawing '{dbChild.Drawing.DrawingNumber}' exists in DB tree under " +
                    $"'{parent.Drawing.DrawingNumber}' but is absent from the import list — " +
                    $"skipped to maintain data consistency.");
                continue;
            }

            leftMatch.PartId      = dbChild.Drawing.PartId;
            leftMatch.Revision    = dbChild.Drawing.Revision;
            leftMatch.Description = dbChild.Drawing.Description;
            leftMatch.IsAssembly  = dbChild.Drawing.IsAssembly;
            if (string.IsNullOrEmpty(leftMatch.PdfPath))
                leftMatch.PdfPath = dbChild.Drawing.PdfPath;

            _leftDrawings.Remove(leftMatch);

            var childNode = new DrawingNode(leftMatch) { PartTreeId = dbChild.PartTreeId };
            if (dbChild.Children.Count > 0)
                AttachDbChildren(childNode, dbChild.Children);

            parent.Children.Add(childNode);
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Info panel ────────────────────────────────────────────────────────

    private void SelectDrawing(DrawingInfo info, DrawingNode? sourceNode = null)
    {
        // Clear left panel multi-selections
        foreach (var d in _selectedDrawings) d.IsSelected = false;
        _selectedDrawings.Clear();

        // Clear previous single selections
        if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }
        if (_selectedDrawing != null) _selectedDrawing.IsSelected = false;

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

    private void InfoFilePath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_infoUpdating || _selectedDrawing == null) return;
        _selectedDrawing.PdfPath = InfoFilePath.Text;
        _hasUnsavedChanges = true;
    }

    private void InfoBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title  = "Select PDF File",
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            InfoFilePath.Text = dialog.FileName;
    }

    private void InfoSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrawing == null) return;
        InfoSaveError.Visibility = Visibility.Collapsed;

        if (_selectedDrawing.PartId == null)
        {
            InfoSaveError.Text = "This drawing has not been linked to the database yet.";
            InfoSaveError.Visibility = Visibility.Visible;
            return;
        }

        int partId = _selectedDrawing.PartId.Value;

        // Save part metadata
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

        // Save file path if provided
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

        // Update in-memory model
        _selectedDrawing.Revision    = InfoRevision.Text.Trim();
        _selectedDrawing.Description = InfoDescription.Text.Trim();
        _selectedDrawing.IsAssembly  = InfoIsAssembly.IsChecked == true;
        _selectedDrawing.PdfPath     = filePath;

        Logger.Instance.Info($"Info panel saved: {_selectedDrawing.DrawingNumber} (partId={partId})");
    }

    // ── Left panel: selection and drag source ─────────────────────────────

    private void LeftItemBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragInProgress) return;
        if (sender is not FrameworkElement el || el.DataContext is not DrawingInfo info) return;

        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

        if (ctrl)
        {
            ApplyCtrlClick(info);
        }
        else if (shift && _anchorDrawing != null)
        {
            ApplyShiftClick(info);
        }
        else
        {
            // Plain click: single select, reset anchor
            _anchorDrawing = info;
            SelectDrawing(info, null);
            _selectedDrawings.Add(info);
        }
    }

    /// <summary>Ctrl+Click: toggle the item in the multi-select set.</summary>
    private void ApplyCtrlClick(DrawingInfo info)
    {
        // Clear tree node selection; clear single drawing from tree context if any
        if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }
        if (_selectedDrawings.Count == 0 && _selectedDrawing != null)
        {
            _selectedDrawing.IsSelected = false;
            _selectedDrawing = null;
        }

        if (_selectedDrawings.Contains(info))
        {
            info.IsSelected = false;
            _selectedDrawings.Remove(info);
        }
        else
        {
            info.IsSelected = true;
            _selectedDrawings.Add(info);
        }

        UpdateInfoPanelForSelection();
    }

    /// <summary>Shift+Click: select the contiguous range from the anchor to this item.</summary>
    private void ApplyShiftClick(DrawingInfo info)
    {
        if (_selectedNode != null) { _selectedNode.IsSelected = false; _selectedNode = null; }

        foreach (var d in _selectedDrawings) d.IsSelected = false;
        _selectedDrawings.Clear();
        if (_selectedDrawing != null) { _selectedDrawing.IsSelected = false; _selectedDrawing = null; }

        var items = _leftDrawings.ToList();
        int anchorIdx  = items.IndexOf(_anchorDrawing!);
        int currentIdx = items.IndexOf(info);
        if (anchorIdx >= 0 && currentIdx >= 0)
        {
            int from = Math.Min(anchorIdx, currentIdx);
            int to   = Math.Max(anchorIdx, currentIdx);
            for (int i = from; i <= to; i++)
            {
                items[i].IsSelected = true;
                _selectedDrawings.Add(items[i]);
            }
        }

        UpdateInfoPanelForSelection();
    }

    /// <summary>
    /// Updates the info panel based on the current multi-select state.
    /// Shows info when exactly one item is selected; clears the panel for multi-selection.
    /// Does NOT touch IsSelected flags — callers manage those.
    /// </summary>
    private void UpdateInfoPanelForSelection()
    {
        if (_selectedDrawings.Count == 1)
        {
            var single = _selectedDrawings[0];
            _selectedDrawing = single;
            _infoUpdating = true;
            InfoDrawingNumber.Text = single.DrawingNumber;
            InfoRevision.Text      = single.Revision;
            InfoDescription.Text   = single.Description;
            InfoQuantity.Text      = single.QuantityInAssembly;
            InfoIsAssembly.IsChecked = single.IsAssembly;
            InfoFilePath.Text      = single.PdfPath;
            InfoPanel.IsEnabled    = true;
            _infoUpdating = false;
        }
        else
        {
            // Multi-select or empty: clear info panel fields without touching IsSelected
            _selectedDrawing = null;
            _infoUpdating = true;
            InfoDrawingNumber.Text   = string.Empty;
            InfoRevision.Text        = string.Empty;
            InfoDescription.Text     = string.Empty;
            InfoQuantity.Text        = string.Empty;
            InfoIsAssembly.IsChecked = false;
            InfoFilePath.Text        = string.Empty;
            InfoPanel.IsEnabled      = false;
            _infoUpdating = false;
        }
    }

    private void LeftOpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is DrawingInfo info)
        {
            // Multi-select: open all selected PDFs; otherwise open only this one
            if (_selectedDrawings.Count > 1 && _selectedDrawings.Contains(info))
                foreach (var d in _selectedDrawings) OpenPdf(d.PdfPath);
            else
                OpenPdf(info.PdfPath);
        }
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

        bool isMultiDrag = _selectedDrawings.Count > 1 && _selectedDrawings.Contains(info);
        List<DrawingInfo> dragList = isMultiDrag ? new List<DrawingInfo>(_selectedDrawings) : new List<DrawingInfo> { info };

        foreach (var d in dragList) d.IsDragging = true;
        _dragInProgress = true;
        ShowDragPopup(isMultiDrag ? $"{dragList.Count} drawings" : info.DrawingNumber);
        try
        {
            var dataObj = new DataObject();
            if (isMultiDrag)
                dataObj.SetData(DragFormatInfoList, dragList);
            else
                dataObj.SetData(DragFormatInfo, info);
            DragDrop.DoDragDrop(DrawingListPanel, dataObj, DragDropEffects.Move);
        }
        finally
        {
            foreach (var d in dragList) d.IsDragging = false;
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

    private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TreeScrollViewer.ScrollToVerticalOffset(TreeScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // ── Tree view: drop target ────────────────────────────────────────────

    private void TreeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent(DragFormatInfo) && !e.Data.GetDataPresent(DragFormatInfoList) && !e.Data.GetDataPresent(DragFormatNode))
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

        if (e.Data.GetDataPresent(DragFormatInfoList))
        {
            if (e.Data.GetData(DragFormatInfoList) is not List<DrawingInfo> list) return;
            foreach (var drawing in list)
            {
                targetNode.Children.Add(new DrawingNode(drawing));
                _leftDrawings.Remove(drawing);
            }
            SortCollection(targetNode.Children);
            _hasUnsavedChanges = true;
            Logger.Instance.Info($"Added {list.Count} drawings under {targetNode.Drawing.DrawingNumber}");
        }
        else if (e.Data.GetDataPresent(DragFormatInfo))
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
            _drawingRepository.SaveTree(_rootNodes);
            _hasUnsavedChanges = false;
            Logger.Instance.Info($"Drawing tree saved to DB for PO: {_poName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save tree: {ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"TreeBuilder save failed: {ex.Message}");
        }
    }

    // ── Return ───────────────────────────────────────────────────────────

    private void AddDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.AddDrawingDialog(_leftDrawings) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;

        _leftDrawings.Add(dialog.Result);
        Logger.Instance.Info($"Manually added drawing: {dialog.Result.DrawingNumber}");
    }

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
