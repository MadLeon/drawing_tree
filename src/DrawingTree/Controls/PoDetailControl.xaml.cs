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
            JobsPanel.Children.Add(BuildJobSection(jobGroup.Key, jobGroup.ToList()));

        Logger.Instance.Info($"PoDetailControl: loaded poId={poId}, {items.Count} order item(s)");
    }

    // ── UI construction ─────────────────────────────────────────────────

    private Border BuildJobSection(string jobNumber, List<PoOrderItemRow> rows)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = $"Job No: {jobNumber}",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var row in rows)
            content.Children.Add(BuildOrderItemBlock(row));

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
    private StackPanel BuildOrderItemBlock(PoOrderItemRow row)
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
                panel.Children.Add(BuildPartRow(node, row.OrderItemId));
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
        foreach (var width in new[] { 180, 60, 260, 28, 28, 80 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

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
        return grid;
    }

    private Grid BuildPartRow(DrawingNode node, int orderItemId)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        foreach (var width in new[] { 180, 60, 260, 28, 28, 80 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        var drawingNumber = new TextBlock
        {
            Text = node.Drawing.DrawingNumber, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
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
            Style = (Style)Resources["IconBtn"], Width = 24, Height = 24, ToolTip = "Open PDF",
            Content = new Path
            {
                Data = (Geometry)Resources["OpenInNewGeo"], Stretch = Stretch.Uniform,
                Fill = System.Windows.Media.Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openPdfBtn.Click += (_, _) => OpenPdfExternal(node.Drawing.PdfPath);
        Grid.SetColumn(openPdfBtn, 3);
        grid.Children.Add(openPdfBtn);

        if (node.Drawing.PartId is int partId)
        {
            var openPartBtn = new Button
            {
                Style = (Style)Resources["IconBtn"], Width = 24, Height = 24, ToolTip = "Open part (coming soon)",
                Content = new Path
                {
                    Data = (Geometry)Resources["BuildGeo"], Stretch = Stretch.Uniform,
                    Fill = System.Windows.Media.Brushes.Gray, Width = 13, Height = 13
                }
            };
            openPartBtn.Click += (_, _) =>
            {
                Logger.Instance.Info($"PoDetailControl: open part requested for partId={partId}, orderItemId={orderItemId}");
                OpenPartRequested?.Invoke(this, (partId, orderItemId));
            };
            Grid.SetColumn(openPartBtn, 4);
            grid.Children.Add(openPartBtn);

            if (node.IsRootNode)
            {
                var viewTreeBtn = new Button { Content = "View Tree", FontSize = 11, Height = 24 };
                viewTreeBtn.Click += (_, _) =>
                {
                    Logger.Instance.Info($"PoDetailControl: view tree requested for partId={partId}");
                    ViewTreeRequested?.Invoke(this, partId);
                };
                Grid.SetColumn(viewTreeBtn, 5);
                grid.Children.Add(viewTreeBtn);
            }
        }

        return grid;
    }

    private DrawingNode BuildRootNode(int partId)
    {
        var info = _drawingRepository.GetDrawingInfo(partId);
        var drawing = new DrawingInfo
        {
            PartId        = partId,
            DrawingNumber = info?.DrawingNumber ?? partId.ToString(),
            Revision      = info?.Revision      ?? string.Empty,
            Description   = info?.Description   ?? string.Empty,
            IsAssembly    = info?.IsAssembly     ?? false,
            PdfPath       = info?.PdfPath        ?? string.Empty
        };
        var node = new DrawingNode(drawing) { IsRootNode = true };

        var children = _poRepository.GetPartTree(partId);
        foreach (var child in children)
            node.Children.Add(child);

        return node;
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
}
