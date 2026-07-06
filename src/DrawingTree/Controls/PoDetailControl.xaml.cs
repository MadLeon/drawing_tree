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

        var header = _poRepository.GetPoHeader(poId);
        TitleLabel.Text = header == null
            ? $"PO #{poId} (not found)"
            : $"P.O. {header.PoNumber}    O.E. {header.OeNumber}";

        JobsPanel.Children.Clear();

        if (header == null)
        {
            Logger.Instance.Error($"PoDetailControl: no PO found for poId={poId}");
            return;
        }

        var items = _poRepository.GetPoOrderItems(poId);
        foreach (var jobGroup in items.GroupBy(i => i.JobNumber))
            JobsPanel.Children.Add(BuildJobSection(jobGroup.Key, jobGroup.ToList(), header.CustomerName));

        Logger.Instance.Info($"PoDetailControl: loaded poId={poId}, {items.Count} order item(s)");
    }

    // ── UI construction ─────────────────────────────────────────────────

    private Border BuildJobSection(string jobNumber, List<PoOrderItemRow> rows, string? customerName)
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

        foreach (var row in rows)
            content.Children.Add(BuildOrderItemBlock(row, customerName));

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
    private StackPanel BuildOrderItemBlock(PoOrderItemRow row, string? customerName)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };

        panel.Children.Add(new TextBlock
        {
            Text = $"Line {row.LineNumber}    Qty {row.Quantity}    " +
                   $"Release Date: {row.ReleaseDate ?? "-"}    Due Date: {row.DueDate ?? "-"}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4)
        });

        if (row.PartId is int partId)
        {
            panel.Children.Add(BuildPartTableHeader());
            var rootNode = BuildRootNode(partId);
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
            ? _partRepository.GetDirAttachmentForOrderItem(dirPartId, orderItemId)
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
        if (string.IsNullOrWhiteSpace(folder)) return null;

        var fileName = $"{node.Drawing.DrawingNumber} Rev{node.Drawing.Revision} {node.Drawing.Description}-ballooned.pdf";
        var candidate = System.IO.Path.Combine(folder, fileName);
        return File.Exists(candidate) ? candidate : null;
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
