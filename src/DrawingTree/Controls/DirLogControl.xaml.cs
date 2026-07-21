/// <summary>
/// DirLogControl.xaml.cs
/// DIR Log page: shows completed DIR file records grouped by day, with PO/Job/Customer
/// context and quick-open buttons for the DIR file and its matching bubble drawing.
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
using TextBox          = System.Windows.Controls.TextBox;
using Path             = System.Windows.Shapes.Path;
using Brushes          = System.Windows.Media.Brushes;
using Color            = System.Windows.Media.Color;
using MessageBox        = System.Windows.MessageBox;
using MessageBoxButton  = System.Windows.MessageBoxButton;
using MessageBoxImage   = System.Windows.MessageBoxImage;

namespace DrawingTree.Controls;

public partial class DirLogControl : UserControl
{
    private static readonly int[] ColumnWidths = { 165, 90, 200, 55, 140, 70, 95, 90, 30, 30 };

    private readonly DirLogRepository _repository     = new();
    private readonly PartRepository   _partRepository  = new();

    private List<DirLogRow> _allRows = new();
    private bool _loaded;

    public DirLogControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        _ = LoadDataAsync();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = LoadDataAsync();

    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = LoadDataAsync();
    }

    // ── Data loading ──────────────────────────────────────────────────────

    private async Task LoadDataAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var (start, end) = DayRadio.IsChecked == true
                ? (today, today)
                : (today.AddDays(-29), today);

            _allRows = await Task.Run(() => _repository.GetDirLogRows(start, end));
            RenderSections();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DirLogControl.LoadDataAsync failed: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    private void RenderSections()
    {
        LogSectionsPanel.Children.Clear();

        if (_allRows.Count == 0)
        {
            LogSectionsPanel.Children.Add(new TextBlock
            {
                Text = "(no DIR records in this range)", FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        // GroupBy is stable, so the SQL's ORDER BY created_at ASC is preserved within each group.
        var dayGroups = _allRows
            .GroupBy(r => r.UpdatedAt[..10])
            .OrderByDescending(g => g.Key);

        foreach (var group in dayGroups)
            LogSectionsPanel.Children.Add(BuildDaySection(group.Key, group.ToList()));
    }

    private UIElement BuildDaySection(string isoDate, List<DirLogRow> rows)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

        var date = DateOnly.ParseExact(isoDate, "yyyy-MM-dd");
        panel.Children.Add(new TextBlock
        {
            Text = date.ToString("MMM d, yyyy"), FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        panel.Children.Add(BuildHeaderRow());
        foreach (var row in rows)
            panel.Children.Add(BuildDataRow(row));

        return panel;
    }

    private static Grid BuildHeaderRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var headers = new[]
        {
            "PO Number", "Job Number", "Drawing Number", "Rev", "Customer",
            "Start", "Finish", "Duration", "", ""
        };
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

    private Grid BuildDataRow(DirLogRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in ColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        AddCell(grid, 0, row.PoNumber);
        AddCell(grid, 1, row.JobNumber);
        AddCell(grid, 2, row.DrawingNumber, ellipsis: true);
        AddCell(grid, 3, row.Revision);
        AddCell(grid, 4, row.CustomerName ?? "-", ellipsis: true);

        var start = DateTime.Parse(row.CreatedAt);
        var finish = DateTime.Parse(row.UpdatedAt);
        var finishDate = DateOnly.FromDateTime(finish);
        AddCell(grid, 5, start.ToString("HH:mm"));
        var durationBlock = AddCell(grid, 7, FormatDuration(start, finish));

        var finishCell = BuildFinishCell(row.DirAttachmentId, finish, finishDate,
            onCommitted: newFinish =>
            {
                finish = newFinish;
                durationBlock.Text = FormatDuration(start, finish);
            });
        Grid.SetColumn(finishCell, 6);
        grid.Children.Add(finishCell);

        var bubBtn = BuildOpenButton(
            enabled: row.BubAttachmentId.HasValue,
            tooltip: row.BubAttachmentId.HasValue ? "Open bubble drawing" : "No bubble file found for this drawing");
        bubBtn.Click += (_, _) => OpenFile(row.BubFilePath!, () => _partRepository.RemoveBubbleAttachment(row.BubAttachmentId!.Value));
        Grid.SetColumn(bubBtn, 8);
        grid.Children.Add(bubBtn);

        var dirBtn = BuildOpenButton(enabled: true, tooltip: "Open DIR file");
        dirBtn.Click += (_, _) => OpenFile(row.DirFilePath, () => _partRepository.RemoveDirAttachment(row.DirAttachmentId));
        Grid.SetColumn(dirBtn, 9);
        grid.Children.Add(dirBtn);

        return grid;
    }

    private static TextBlock AddCell(Grid grid, int column, string text, bool ellipsis = false)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
        if (ellipsis)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.ToolTip = text;
        }
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        return block;
    }

    /// <summary>
    /// Builds the editable "Finish" cell: a time-only (HH:mm) TextBox that persists edits to
    /// part_attachment.updated_at on LostFocus, with an inline success indicator.
    /// </summary>
    /// <param name="attachmentId">part_attachment.id (DIR row) to update</param>
    /// <param name="initialFinish">Current Finish value shown in the row</param>
    /// <param name="finishDate">Date portion kept fixed; only the time-of-day is editable</param>
    /// <param name="onCommitted">Invoked with the new Finish value after a successful save</param>
    private Grid BuildFinishCell(int attachmentId, DateTime initialFinish, DateOnly finishDate, Action<DateTime> onCommitted)
    {
        var cell = new Grid();

        var textBox = new TextBox
        {
            Text = initialFinish.ToString("HH:mm"),
            FontSize = 12, Padding = new Thickness(2, 1, 2, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 42, HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };

        var checkIcon = new Path
        {
            Data = (Geometry)FindResource("CheckCircleGeo"), Stretch = Stretch.Uniform,
            Fill = new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)), Width = 12, Height = 12,
            Margin = new Thickness(46, 0, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        textBox.LostFocus += (_, _) =>
        {
            if (!DateTime.TryParseExact(textBox.Text.Trim(), "HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedTime))
            {
                MessageBox.Show($"Invalid time: \"{textBox.Text}\". Expected format HH:mm.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                textBox.Text = initialFinish.ToString("HH:mm");
                checkIcon.Visibility = Visibility.Collapsed;
                return;
            }

            var newFinish = finishDate.ToDateTime(TimeOnly.FromDateTime(parsedTime));
            if (newFinish == initialFinish) return;

            if (_repository.UpdateFinishTime(attachmentId, newFinish))
            {
                initialFinish = newFinish;
                checkIcon.Visibility = Visibility.Visible;
                onCommitted(newFinish);
            }
            else
            {
                MessageBox.Show("Failed to save the new Finish time.",
                    "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                textBox.Text = initialFinish.ToString("HH:mm");
                checkIcon.Visibility = Visibility.Collapsed;
            }
        };

        cell.Children.Add(textBox);
        cell.Children.Add(checkIcon);
        return cell;
    }

    private Button BuildOpenButton(bool enabled, string tooltip)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(2, 0, 2, 0),
            ToolTip = tooltip, IsEnabled = enabled,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Content = new Path
            {
                Data = (Geometry)FindResource("OpenInNewGeo"), Stretch = Stretch.Uniform,
                Fill = enabled ? Brushes.DodgerBlue : Brushes.Gray, Width = 13, Height = 13
            }
        };
        return btn;
    }

    private static string FormatDuration(DateTime start, DateTime finish)
    {
        var minutes = Math.Max(0, (int)(finish - start).TotalMinutes);
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60}h {minutes % 60}m";
    }

    // ── File opening ─────────────────────────────────────────────────────

    private void OpenFile(string filePath, Action removeFromDb)
    {
        if (!File.Exists(filePath))
        {
            var choice = MessageBox.Show(
                $"File not found:\n{filePath}\n\nRemove this entry from the database?",
                "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                removeFromDb();
                _ = LoadDataAsync();
            }
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
