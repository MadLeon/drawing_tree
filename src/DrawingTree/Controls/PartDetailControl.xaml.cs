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
using DrawingTree.Services;

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
    private readonly PoRepository   _poRepository   = new();

    private int       _partId;
    private int       _orderItemId;
    private MpContext? _mpContext;

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

        if (_orderItemId > 0)
            _mpContext = _poRepository.GetMpContext(_orderItemId);

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

        if (_mpContext != null)
        {
            PoInfoPanel.Visibility = Visibility.Visible;
            PoNumberText.Text = _mpContext.PoNumber;
            JobNumberText.Text = _mpContext.JobNumber;
            LineText.Text = _mpContext.LineNumber.ToString();
        }

        LoadPdfFiles();
        LoadMpSection();
        LoadProcessSteps();
        LoadNotes();

        Logger.Instance.Info($"PartDetailControl: loaded partId={partId}, orderItemId={orderItemId}");
    }

    // ── Manufacturing Process ────────────────────────────────────────────

    private void LoadMpSection()
    {
        MpFilesPanel.Children.Clear();

        NewMpButton.Visibility = _mpContext != null ? Visibility.Visible : Visibility.Collapsed;

        var attachments = _partRepository.GetMpAttachments(_partId);

        if (attachments.Count == 0)
        {
            MpFilesPanel.Children.Add(new TextBlock
            {
                Text = "(no MP files yet)", FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        MpFilesPanel.Children.Add(BuildMpFilesHeader());
        foreach (var attachment in attachments)
            MpFilesPanel.Children.Add(BuildMpFileRow(attachment));
    }

    private static Grid BuildMpFilesHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var header = new TextBlock
        {
            Text = "File Name", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
        };
        Grid.SetColumn(header, 0);
        grid.Children.Add(header);

        return grid;
    }

    private Grid BuildMpFileRow(MpAttachmentRow attachment)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nameText = new TextBlock
        {
            Text = attachment.FileName, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = attachment.FilePath
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        var openBtn = new Button
        {
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(2, 0, 2, 0), ToolTip = "Open",
            Content = new Path
            {
                Data = (Geometry)Resources["OpenInNewGeo"], Stretch = Stretch.Uniform,
                Fill = Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        openBtn.Click += (_, _) => OpenMpFile(attachment);
        Grid.SetColumn(openBtn, 1);
        grid.Children.Add(openBtn);

        return grid;
    }

    private void OpenMpFile(MpAttachmentRow attachment)
    {
        if (!File.Exists(attachment.FilePath))
        {
            var choice = MessageBox.Show(
                $"File not found:\n{attachment.FilePath}\n\nRemove this entry from the database?",
                "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                _partRepository.RemoveMpAttachment(attachment.AttachmentId);
                LoadMpSection();
            }
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = attachment.FilePath, UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewMpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mpContext == null) return;

        try
        {
            MpFileService.CreateAndOpen(_mpContext);
            LoadMpSection();
        }
        catch (MpFolderNotConfiguredException ex)
        {
            Logger.Instance.Warning($"PartDetailControl: MP folder not configured for '{ex.CustomerName}'");
            MessageBox.Show(
                $"No MP folder configured for customer '{ex.CustomerName}'.\n\n" +
                $"Please open config.txt and set the folder name under [CustomerFolderMappings]:\n" +
                $"  {ex.CustomerName}=\n\n" +
                $"Config file:\n{ex.ConfigFilePath}",
                "MP Folder Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Instance.Warning($"PartDetailControl: MP file already exists: {ex.Message}");
            MessageBox.Show(ex.Message, "File Already Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartDetailControl: failed to create MP file: {ex.Message}");
            MessageBox.Show($"Failed to create MP file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        PdfListPanel.Children.Add(BuildPdfHeader());
        foreach (var file in files)
            PdfListPanel.Children.Add(BuildPdfRow(file));
    }

    private static readonly int[] PdfColumnWidths = { 260, 60, 70, 140, 28 };

    private static Grid BuildPdfHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in PdfColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var headers = new[] { "File Name", "Rev", "Active", "Last Modified", "PDF" };
        for (int i = 0; i < headers.Length; i++)
        {
            var block = new TextBlock
            {
                Text = headers[i], FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
            };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }
        return grid;
    }

    private Grid BuildPdfRow(PartDrawingFile file)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in PdfColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

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
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(2, 0, 2, 0), ToolTip = "Open PDF",
            Content = new Path
            {
                Data = (Geometry)Resources["OpenInNewGeo"], Stretch = Stretch.Uniform,
                Fill = Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

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

    private static readonly GridLength[] StepColumnWidths =
    {
        new GridLength(40),
        new GridLength(80),
        new GridLength(1, GridUnitType.Star), // Description fills remaining width (~65% of section)
        new GridLength(140),
        new GridLength(90),
        new GridLength(90),
        new GridLength(90),
        new GridLength(110),
        new GridLength(110)
    };

    private static Grid BuildProcessStepRow(ProcessStepRow step)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        foreach (var width in StepColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

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
