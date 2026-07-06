/// <summary>
/// PoDetailControl.xaml.cs
/// Single-PO detail page: lists every job/order_item under a PO, each line
/// showing its BOM as a flat list (drawing number, revision, description).
/// </summary>

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Models;
using DrawingTree.Services;

using UserControl      = System.Windows.Controls.UserControl;
using Button           = System.Windows.Controls.Button;
using Path             = System.Windows.Shapes.Path;
using MessageBox        = System.Windows.MessageBox;
using MessageBoxButton  = System.Windows.MessageBoxButton;
using MessageBoxImage   = System.Windows.MessageBoxImage;

namespace DrawingTree.Controls;

public partial class PoDetailControl : UserControl
{
    private readonly PoRepository _poRepository = new();
    private readonly DrawingRepository _drawingRepository = new();
    private readonly PartRepository _partRepository = new();

    private int _currentPoId;
    private Dictionary<(int PartId, int OrderItemId), PartAttachmentRow?> _dirCache = new();
    private List<DocumentPackageExportService.ExportRow> _exportRows = new();
    private string _currentPoNumber = string.Empty;

    public event EventHandler? BackRequested;
    public event EventHandler<int>? ViewTreeRequested;
    public event EventHandler<(int PartId, int OrderItemId)>? OpenPartRequested;

    public PoDetailControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads the PO header and all job/order_item rows for the given PO and renders the page.
    /// </summary>
    /// <param name="poId">purchase_order.id</param>
    public void LoadPo(int poId)
    {
        _currentPoId = poId;
        _dirCache = new Dictionary<(int PartId, int OrderItemId), PartAttachmentRow?>();
        _exportRows = new List<DocumentPackageExportService.ExportRow>();

        var header = _poRepository.GetPoHeader(poId);
        JobsPanel.Children.Clear();

        if (header == null)
        {
            TitleLabel.Text = $"PO #{poId} (not found)";
            Logger.Instance.Error($"PoDetailControl: no PO found for poId={poId}");
            return;
        }

        _currentPoNumber = header.PoNumber;

        var items = _poRepository.GetPoOrderItems(poId);

        // Build each row's part tree once; reuse for both completion tallies and BOM rendering.
        var rowRootNodes = new Dictionary<int, DrawingNode>();
        var jobTallies = new Dictionary<string, (int Total, int Done)>();
        int poTotal = 0, poDone = 0;

        foreach (var row in items)
        {
            if (row.PartId is not int partId) continue;

            var rootNode = BuildRootNode(partId);
            rowRootNodes[row.OrderItemId] = rootNode;

            int total = 0, done = 0;
            foreach (var node in Flatten(rootNode))
            {
                total++;
                if (IsDirDone(node, row.OrderItemId)) done++;

                var dirRow = node.Drawing.PartId is int partIdForDir
                    ? GetDirCached(partIdForDir, row.OrderItemId)
                    : null;
                _exportRows.Add(new DocumentPackageExportService.ExportRow(
                    row.JobNumber,
                    node.Drawing.DrawingNumber,
                    Completed: dirRow != null && string.Equals(dirRow.Status, "completed", StringComparison.OrdinalIgnoreCase),
                    Reviewed: dirRow != null && string.Equals(dirRow.Status, "reviewed", StringComparison.OrdinalIgnoreCase)));
            }

            poTotal += total;
            poDone += done;

            var (jobTotal, jobDone) = jobTallies.TryGetValue(row.JobNumber, out var t) ? t : (0, 0);
            jobTallies[row.JobNumber] = (jobTotal + total, jobDone + done);
        }

        TitleLabel.Text = $"P.O. {header.PoNumber}    O.E. {header.OeNumber}{FormatCompletionSuffix(poTotal, poDone)}";

        foreach (var jobGroup in items.GroupBy(i => i.JobNumber))
        {
            var (jobTotal, jobDone) = jobTallies.TryGetValue(jobGroup.Key, out var t) ? t : (0, 0);
            JobsPanel.Children.Add(BuildJobSection(jobGroup.Key, jobGroup.ToList(), header.CustomerName,
                rowRootNodes, jobTotal, jobDone));
        }

        Logger.Instance.Info($"PoDetailControl: loaded poId={poId}, {items.Count} order item(s)");
    }

    /// <summary>Returns true if the given node's part has a DIR attachment (for the given order item) marked completed or reviewed.</summary>
    private bool IsDirDone(DrawingNode node, int orderItemId)
    {
        if (node.Drawing.PartId is not int partId) return false;
        var dirRow = GetDirCached(partId, orderItemId);
        return dirRow != null &&
               (string.Equals(dirRow.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dirRow.Status, "reviewed", StringComparison.OrdinalIgnoreCase));
    }

    private PartAttachmentRow? GetDirCached(int partId, int orderItemId)
    {
        var key = (partId, orderItemId);
        if (!_dirCache.TryGetValue(key, out var row))
        {
            row = _partRepository.GetDirAttachmentForOrderItem(partId, orderItemId);
            _dirCache[key] = row;
        }
        return row;
    }

    /// <summary>Formats a "    Completion: {pct}%" suffix (one decimal place), or "" if there are no drawings to count.</summary>
    private static string FormatCompletionSuffix(int total, int done)
    {
        if (total == 0) return string.Empty;
        double pct = (double)done / total * 100;
        return $"    Completion: {pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%";
    }

    // ── UI construction ─────────────────────────────────────────────────

    private Border BuildJobSection(string jobNumber, List<PoOrderItemRow> rows, string? customerName,
        Dictionary<int, DrawingNode> rowRootNodes, int jobTotal, int jobDone)
    {
        var content = new StackPanel();

        var jobHeader = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        jobHeader.Children.Add(new TextBlock
        {
            Text = $"Job No: {jobNumber}",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });

        var firstPartId = rows.FirstOrDefault(r => r.PartId.HasValue)?.PartId;
        if (firstPartId.HasValue)
        {
            var viewTreeBtn = new Button
            {
                Content = "Tree", FontSize = 11, Height = 24,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            viewTreeBtn.Click += (_, _) =>
            {
                Logger.Instance.Info($"PoDetailControl: view tree requested for partId={firstPartId.Value}");
                ViewTreeRequested?.Invoke(this, firstPartId.Value);
            };
            jobHeader.Children.Add(viewTreeBtn);
        }

        content.Children.Add(jobHeader);

        string completionSuffix = FormatCompletionSuffix(jobTotal, jobDone);
        foreach (var row in rows)
            content.Children.Add(BuildOrderItemBlock(row, customerName,
                rowRootNodes.GetValueOrDefault(row.OrderItemId), completionSuffix));

        return new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content
        };
    }

    /// <summary>
    /// Builds one order_item's display block: line/date header (detached from the table)
    /// followed by a flat (non-tree) list of the part's drawing number plus all BOM descendants.
    /// </summary>
    private StackPanel BuildOrderItemBlock(PoOrderItemRow row, string? customerName,
        DrawingNode? rootNode, string completionSuffix)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };

        panel.Children.Add(new TextBlock
        {
            Text = $"Line {row.LineNumber}    Qty {row.Quantity}    " +
                   $"Release Date: {row.ReleaseDate ?? "-"}    Due Date: {row.DueDate ?? "-"}" +
                   completionSuffix,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        if (rootNode != null)
        {
            panel.Children.Add(BuildPartTableHeader());
            foreach (var node in Flatten(rootNode))
                panel.Children.Add(BuildPartRow(node, row.OrderItemId, customerName));
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "(no part linked)",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray
            });
        }

        return panel;
    }

    private static Grid BuildPartTableHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        void AddHeader(int col, string text)
        {
            var block = new TextBlock
            {
                Text = text, FontWeight = FontWeights.SemiBold, FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray
            };
            Grid.SetColumn(block, col);
            grid.Children.Add(block);
        }

        AddHeader(0, "Drawing Number");
        AddHeader(1, "Rev.");
        AddHeader(2, "Description");
        AddHeader(3, "PDF");
        AddHeader(4, "DIR");
        AddHeader(5, "Bub.");
        AddHeader(6, "Status");
        return grid;
    }

    private Grid BuildPartRow(DrawingNode node, int orderItemId, string? customerName)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        var hasPart = node.Drawing.PartId.HasValue;
        var drawingNumber = new TextBlock
        {
            Text = node.Drawing.DrawingNumber, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = hasPart ? System.Windows.Media.Brushes.DodgerBlue : System.Windows.Media.Brushes.Black,
            TextDecorations = hasPart ? TextDecorations.Underline : null,
            Cursor = hasPart ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
        };
        if (node.Drawing.PartId is int linkedPartId)
        {
            drawingNumber.MouseLeftButtonDown += (_, _) =>
            {
                Logger.Instance.Info($"PoDetailControl: open part requested for partId={linkedPartId}, orderItemId={orderItemId}");
                OpenPartRequested?.Invoke(this, (linkedPartId, orderItemId));
            };
        }
        Grid.SetColumn(drawingNumber, 0);
        grid.Children.Add(drawingNumber);

        var revision = new TextBlock
        {
            Text = node.Drawing.Revision, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(revision, 1);
        grid.Children.Add(revision);

        var description = new TextBlock
        {
            Text = node.Drawing.Description, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = node.Drawing.Description
        };
        Grid.SetColumn(description, 2);
        grid.Children.Add(description);

        var openPdfBtn = new Button
        {
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(3), ToolTip = "Open PDF",
            Content = new Path
            {
                Data = (Geometry)Resources["OpenInNewGeo"], Stretch = Stretch.Uniform,
                Fill = System.Windows.Media.Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openPdfBtn.Click += (_, _) => OpenFileExternal(node.Drawing.PdfPath);
        Grid.SetColumn(openPdfBtn, 3);
        grid.Children.Add(openPdfBtn);

        PartAttachmentRow? dirRow = node.Drawing.PartId is int dirPartId
            ? GetDirCached(dirPartId, orderItemId)
            : null;

        if (dirRow != null)
        {
            var dirBtn = new Button
            {
                Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(3), ToolTip = "Open DIR",
                Content = new Path
                {
                    Data = (Geometry)Resources["AssignmentGeo"], Stretch = Stretch.Uniform,
                    Fill = System.Windows.Media.Brushes.DodgerBlue, Width = 13, Height = 13
                }
            };
            dirBtn.Click += (_, _) => OpenFileExternal(dirRow.FilePath);
            Grid.SetColumn(dirBtn, 4);
            grid.Children.Add(dirBtn);
        }

        string? bubblePath = ResolveBubblePath(node, dirRow, customerName);
        if (bubblePath != null)
        {
            var bubBtn = new Button
            {
                Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(3), ToolTip = "Open bubble drawing",
                Content = new Path
                {
                    Data = (Geometry)Resources["ImageGeo"], Stretch = Stretch.Uniform,
                    Fill = System.Windows.Media.Brushes.DodgerBlue, Width = 13, Height = 13
                }
            };
            bubBtn.Click += (_, _) => OpenFileExternal(bubblePath);
            Grid.SetColumn(bubBtn, 5);
            grid.Children.Add(bubBtn);
        }

        var statusPanel = BuildStatusCell(dirRow);
        Grid.SetColumn(statusPanel, 6);
        grid.Children.Add(statusPanel);

        return grid;
    }

    /// <summary>
    /// Resolves the bubble drawing PDF path for a row: prefers a recorded BUBBLE attachment,
    /// otherwise falls back to a naming-convention lookup under the customer's bubble folder
    /// (only when the row already has a DIR attachment).
    /// </summary>
    private string? ResolveBubblePath(DrawingNode node, PartAttachmentRow? dirRow, string? customerName)
    {
        if (node.Drawing.PartId is not int partId) return null;

        var bubbleRow = _partRepository.GetBubbleAttachment(partId);
        if (bubbleRow != null) return bubbleRow.FilePath;

        if (dirRow == null || string.IsNullOrWhiteSpace(customerName)) return null;

        var folder = BubbleConfig.GetBubbleFolder(customerName);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        // Matches by drawing number + revision only: part.description can be edited after the
        // bubble drawing PDF was created/named, so requiring an exact description match would
        // miss files that still carry the original (pre-edit) description text.
        var searchPattern = $"{node.Drawing.DrawingNumber} Rev{node.Drawing.Revision} *-ballooned.pdf";
        return Directory.GetFiles(folder, searchPattern).FirstOrDefault();
    }

    private StackPanel BuildStatusCell(PartAttachmentRow? dirRow)
    {
        var statusPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        if (dirRow == null)
        {
            statusPanel.Children.Add(new TextBlock
            {
                Text = "N/A", FontSize = 12, Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            });
            return statusPanel;
        }

        statusPanel.Children.Add(new TextBlock
        {
            Text = dirRow.Status, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        });

        if (string.Equals(dirRow.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var attachmentId = dirRow.AttachmentId;
            var reviewBtn = new Button
            {
                Content = "Reviewed", FontSize = 11, Height = 22,
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0)
            };
            reviewBtn.Click += (_, _) =>
            {
                _partRepository.UpdateAttachmentStatus(attachmentId, "reviewed");
                LoadPo(_currentPoId);
            };
            statusPanel.Children.Add(reviewBtn);
        }

        return statusPanel;
    }

    private DrawingNode BuildRootNode(int partId)
    {
        var info = _drawingRepository.GetDrawingInfo(partId);
        var display = info != null
            ? (_drawingRepository.GetDrawingInfo(info.DrawingNumber) ?? info)
            : null;

        var drawing = new DrawingInfo
        {
            PartId        = display?.PartId ?? partId,
            DrawingNumber = display?.DrawingNumber ?? partId.ToString(),
            Revision      = display?.Revision      ?? string.Empty,
            Description   = display?.Description   ?? string.Empty,
            IsAssembly    = display?.IsAssembly     ?? false,
            PdfPath       = display?.PdfPath        ?? string.Empty
        };
        var node = new DrawingNode(drawing) { IsRootNode = true };

        var children = _poRepository.GetPartTree(partId);
        foreach (var child in children)
        {
            NormalizeToLatestPartId(child);
            node.Children.Add(child);
        }

        return node;
    }

    /// <summary>
    /// Rewrites Drawing.PartId to the latest-revision part id for this node and all descendants.
    /// GetPartTree() keeps the tree-linked (possibly stale) part_tree.child_id so
    /// TreeBuilderControl can diff/save relationships correctly; PoDetailControl only displays
    /// and never saves this tree, so it's safe to redirect navigation/attachment lookups here.
    /// </summary>
    private void NormalizeToLatestPartId(DrawingNode node)
    {
        var latest = _drawingRepository.GetDrawingInfo(node.Drawing.DrawingNumber);
        if (latest != null)
            node.Drawing.PartId = latest.PartId;

        foreach (var child in node.Children)
            NormalizeToLatestPartId(child);
    }

    /// <summary>Depth-first flattening of a node and all its descendants (root first).</summary>
    private static IEnumerable<DrawingNode> Flatten(DrawingNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            $"This will clear the contents of\n{DocumentPackageExportService.TargetPath}\nand fill it with the document package for P.O. {_currentPoNumber}.\n" +
            "The file will be left open for you to review and save manually.\n\nContinue?",
            "Export Document Package", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            DocumentPackageExportService.Export(_currentPoNumber, _exportRows);
            Logger.Instance.Info($"PoDetailControl: exported document package for poId={_currentPoId}, {_exportRows.Count} row(s)");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoDetailControl: export failed for poId={_currentPoId}", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void OpenFileExternal(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show("File not found.", "File Not Found",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
