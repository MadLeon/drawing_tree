/// <summary>
/// PartDetailControl.xaml.cs
/// Part detail page: general drawing info, PDF file list, process template steps
/// (augmented with step_tracker execution data for the source order_item), and notes.
/// </summary>

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingTree.Data;
using DrawingTree.Logging;

using UserControl      = System.Windows.Controls.UserControl;
using Button           = System.Windows.Controls.Button;
using Path             = System.Windows.Shapes.Path;
using Brushes          = System.Windows.Media.Brushes;
using MessageBox        = System.Windows.MessageBox;
using MessageBoxButton  = System.Windows.MessageBoxButton;
using MessageBoxImage   = System.Windows.MessageBoxImage;

namespace DrawingTree.Controls;

public partial class PartDetailControl : UserControl
{
    private readonly PartRepository _partRepository = new();

    private int _partId;
    private int _orderItemId;

    public event EventHandler? BackRequested;
    public event EventHandler<int>? ViewTreeRequested;

    public PartDetailControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads the part's general info, PDF files, process steps (for the given order item)
    /// and notes, then renders the page.
    /// </summary>
    /// <param name="partId">part.id</param>
    /// <param name="orderItemId">order_item.id whose step_tracker execution data to show</param>
    public void LoadPart(int partId, int orderItemId)
    {
        _partId = partId;
        _orderItemId = orderItemId;

        var header = _partRepository.GetPartHeader(partId);
        TitleLabel.Text = header == null
            ? $"Drawing Number: (part #{partId} not found)"
            : $"Drawing Number: {header.DrawingNumber}";

        RevisionText.Text = header?.Revision ?? string.Empty;
        DescriptionText.Text = header?.Description ?? string.Empty;
        IsAssemblyText.Text = header?.IsAssembly switch
        {
            true => "Yes",
            false => "No",
            null => "Unknown"
        };

        LoadPdfFiles();
        LoadProcessSteps();
        LoadNotes();

        Logger.Instance.Info($"PartDetailControl: loaded partId={partId}, orderItemId={orderItemId}");
    }

    // ── Drawing PDF ──────────────────────────────────────────────────────

    private void LoadPdfFiles()
    {
        PdfListPanel.Children.Clear();
        var files = _partRepository.GetDrawingFiles(_partId);
        if (files.Count == 0)
        {
            PdfListPanel.Children.Add(new TextBlock
            {
                Text = "(no PDF files found)", FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        foreach (var file in files)
            PdfListPanel.Children.Add(BuildPdfRow(file));
    }

    private Grid BuildPdfRow(PartDrawingFile file)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var width in new[] { 260, 60, 70, 140, 28 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        var nameText = new TextBlock
        {
            Text = file.FileName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = file.FilePath
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        var revisionText = new TextBlock
        {
            Text = file.Revision, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(revisionText, 1);
        grid.Children.Add(revisionText);

        var activeText = new TextBlock
        {
            Text = file.IsActive ? "Active" : string.Empty, FontSize = 12,
            Foreground = Brushes.SeaGreen, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(activeText, 2);
        grid.Children.Add(activeText);

        var modifiedText = new TextBlock
        {
            Text = file.LastModifiedAt ?? string.Empty, FontSize = 12,
            Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(modifiedText, 3);
        grid.Children.Add(modifiedText);

        var openBtn = new Button
        {
            Style = (Style)Resources["IconBtn"], Width = 24, Height = 24, ToolTip = "Open PDF",
            Content = new Path
            {
                Data = (Geometry)Resources["OpenInNewGeo"], Stretch = Stretch.Uniform,
                Fill = Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openBtn.Click += (_, _) => OpenPdfExternal(file.FilePath);
        Grid.SetColumn(openBtn, 4);
        grid.Children.Add(openBtn);

        return grid;
    }

    // ── Process Template ────────────────────────────────────────────────

    private void LoadProcessSteps()
    {
        ProcessStepsPanel.Children.Clear();
        var steps = _partRepository.GetProcessSteps(_partId, _orderItemId);
        if (steps.Count == 0)
        {
            ProcessStepsPanel.Children.Add(new TextBlock
            {
                Text = "(no process template defined)", FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        ProcessStepsPanel.Children.Add(BuildProcessStepsHeader());
        foreach (var step in steps)
            ProcessStepsPanel.Children.Add(BuildProcessStepRow(step));
    }

    private static Grid BuildProcessStepsHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var width in StepColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        void AddHeader(int col, string text)
        {
            var block = new TextBlock
            {
                Text = text, FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
            };
            Grid.SetColumn(block, col);
            grid.Children.Add(block);
        }

        var headers = new[] { "Row", "Shop Code", "Description", "Remark", "Operator", "Machine", "Status", "Start", "End" };
        for (int i = 0; i < headers.Length; i++)
            AddHeader(i, headers[i]);

        return grid;
    }

    private static readonly int[] StepColumnWidths = { 40, 80, 180, 140, 90, 90, 90, 110, 110 };

    private static Grid BuildProcessStepRow(ProcessStepRow step)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        foreach (var width in StepColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        var values = new[]
        {
            step.RowNumber.ToString(), step.ShopCode, step.Description ?? string.Empty,
            step.Remark ?? string.Empty, step.OperatorId ?? string.Empty, step.MachineId ?? string.Empty,
            step.Status ?? string.Empty, step.StartTime ?? string.Empty, step.EndTime ?? string.Empty
        };

        for (int i = 0; i < values.Length; i++)
        {
            var block = new TextBlock
            {
                Text = values[i], FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }

        return grid;
    }

    // ── Notes ────────────────────────────────────────────────────────────

    private void LoadNotes()
    {
        NotesListPanel.Children.Clear();
        var notes = _partRepository.GetPartNotes(_partId);
        if (notes.Count == 0)
        {
            NotesListPanel.Children.Add(new TextBlock
            {
                Text = "(no notes yet)", FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        foreach (var note in notes)
        {
            NotesListPanel.Children.Add(new TextBlock
            {
                Text = $"[{note.CreatedAt}] {note.Author ?? "unknown"}: {note.Content}",
                FontSize = 12, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void AddNoteButton_Click(object sender, RoutedEventArgs e)
    {
        var content = NewNoteTextBox.Text.Trim();
        if (content.Length == 0) return;

        _partRepository.AddPartNote(_partId, content);
        NewNoteTextBox.Text = string.Empty;
        LoadNotes();
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void TreeViewButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info($"PartDetailControl: tree view requested for partId={_partId}");
        ViewTreeRequested?.Invoke(this, _partId);
    }

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
