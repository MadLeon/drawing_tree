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
using DrawingTree.Dialogs;
using DrawingTree.Logging;
using DrawingTree.Models;
using DrawingTree.Services;

using UserControl      = System.Windows.Controls.UserControl;
using Button           = System.Windows.Controls.Button;
using TextBox          = System.Windows.Controls.TextBox;
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
    private PartHeader? _header;

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
        _header = header;

        // Child parts (BOM sub-components) share their parent's order_item_id, since they have no
        // order_item of their own. GetMpContext resolves part fields via order_item.part_id, which
        // in that case points at the PARENT part — patch them back to the part actually being viewed
        // so New Dir/New MP build files for this part, not its parent.
        if (_mpContext != null && header != null && _mpContext.PartId != partId)
        {
            _mpContext = _mpContext with
            {
                PartId = header.PartId,
                DrawingNumber = header.DrawingNumber,
                Revision = header.Revision,
                Description = header.Description
            };
        }
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
        LoadDirSection();
        LoadBubbleSection();
        LoadProcessSteps();
        LoadNotes();

        Logger.Instance.Info($"PartDetailControl: loaded partId={partId}, orderItemId={orderItemId}");
    }

    // ── Manufacturing Process ────────────────────────────────────────────

    private void LoadMpSection()
    {
        MpFilesPanel.Children.Clear();

        var hasOrderItemMp = _mpContext != null && _partRepository.HasOrderItemMpAttachment(_partId, _orderItemId);
        NewMpButton.Visibility = _mpContext != null && !hasOrderItemMp ? Visibility.Visible : Visibility.Collapsed;
        OpenMpFolderButton.Visibility = _orderItemId > 0 ? Visibility.Visible : Visibility.Collapsed;

        var attachments = _partRepository.GetMpAttachments(_partId);

        if (attachments.Count == 0)
        {
            MpFilesPanel.Children.Add(new TextBox
            {
                Text = "(no MP files yet)", Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        MpFilesPanel.Children.Add(BuildMpFilesHeader());
        foreach (var attachment in attachments)
            MpFilesPanel.Children.Add(BuildMpFileRow(attachment));
    }

    private Grid BuildMpFilesHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var header = new TextBox
        {
            Text = "File Name", Style = (Style)FindResource("SelectableText"),
            FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
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

        var nameText = new TextBox
        {
            Text = attachment.FileName, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = attachment.FilePath
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

    private void OpenMpFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mpContext == null)
        {
            MessageBox.Show("Could not resolve order item information for this part.", "Order Item Not Found",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var folder = MpFileService.ResolveFolder(_mpContext);
            if (!Directory.Exists(folder))
            {
                MessageBox.Show($"Folder does not exist yet:\n{folder}", "Folder Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
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
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartDetailControl: failed to open MP folder: {ex.Message}");
            MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Error",
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

    // ── DIR ─────────────────────────────────────────────────────────────

    private static readonly int[] DirColumnWidths = { 340, 50, 170, 110, 140, 28 };

    private void LoadDirSection()
    {
        DirFilesPanel.Children.Clear();

        var hasOrderItemDir = _mpContext != null && _partRepository.GetDirAttachmentForOrderItem(_partId, _orderItemId) != null;
        NewDirButton.Visibility = _mpContext != null && !hasOrderItemDir ? Visibility.Visible : Visibility.Collapsed;
        OpenDirFolderButton.Visibility = _orderItemId > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_header == null) return;

        var attachments = _partRepository.GetDirAttachmentsByDrawingNumber(_header.DrawingNumber);
        if (attachments.Count == 0)
        {
            DirFilesPanel.Children.Add(new TextBox
            {
                Text = "(no DIR files yet)", Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        DirFilesPanel.Children.Add(BuildDirFilesHeader());
        foreach (var attachment in attachments)
            DirFilesPanel.Children.Add(BuildDirFileRow(attachment));
    }

    private Grid BuildDirFilesHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in DirColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var headers = new[] { "File Name", "Rev", "Status", "Type", "Created at", "" };
        for (int i = 0; i < headers.Length; i++)
        {
            var block = new TextBox
            {
                Text = headers[i], Style = (Style)FindResource("SelectableText"),
                FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
            };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }
        return grid;
    }

    private Grid BuildDirFileRow(DirAttachmentRow attachment)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in DirColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var nameText = new TextBox
        {
            Text = attachment.FileName, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = attachment.FilePath
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        var revText = new TextBox
        {
            Text = attachment.Revision, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(revText, 1);
        grid.Children.Add(revText);

        var statusCell = BuildDirStatusCell(attachment);
        Grid.SetColumn(statusCell, 2);
        grid.Children.Add(statusCell);

        var isCurrentOrder = attachment.OrderItemId.HasValue && attachment.OrderItemId.Value == _orderItemId;
        var typeText = new TextBox
        {
            Text = isCurrentOrder ? "current order" : "archived", Style = (Style)FindResource("SelectableText"),
            FontSize = 12,
            FontWeight = isCurrentOrder ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isCurrentOrder ? Brushes.SeaGreen : Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(typeText, 3);
        grid.Children.Add(typeText);

        var createdText = new TextBox
        {
            Text = attachment.CreatedAt, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(createdText, 4);
        grid.Children.Add(createdText);

        var openBtn = new Button
        {
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(2, 0, 2, 0), ToolTip = "Open",
            Content = new Path
            {
                Data = (Geometry)FindResource("OpenInNewGeo"), Stretch = Stretch.Uniform,
                Fill = Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        openBtn.Click += (_, _) => OpenDirFile(attachment);
        Grid.SetColumn(openBtn, 5);
        grid.Children.Add(openBtn);

        return grid;
    }

    private StackPanel BuildDirStatusCell(DirAttachmentRow attachment)
    {
        var statusPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        statusPanel.Children.Add(new TextBox
        {
            Text = attachment.Status, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (string.Equals(attachment.Status, "in progress", StringComparison.OrdinalIgnoreCase))
        {
            var attachmentId = attachment.AttachmentId;
            var completeBtn = new Button
            {
                Content = "Complete", FontSize = 11, Height = 22,
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0)
            };
            completeBtn.Click += (_, _) =>
            {
                _partRepository.UpdateAttachmentStatus(attachmentId, "completed");
                LoadDirSection();
            };
            statusPanel.Children.Add(completeBtn);
        }

        return statusPanel;
    }

    private void OpenDirFile(DirAttachmentRow attachment)
    {
        if (!File.Exists(attachment.FilePath))
        {
            var choice = MessageBox.Show(
                $"File not found:\n{attachment.FilePath}\n\nRemove this entry from the database?",
                "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                _partRepository.RemoveDirAttachment(attachment.AttachmentId);
                LoadDirSection();
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

    private void OpenDirFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mpContext == null)
        {
            MessageBox.Show("Could not resolve order item information for this part.", "Order Item Not Found",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var folder = DirFileService.ResolveFolder(_mpContext);
            if (!Directory.Exists(folder))
            {
                MessageBox.Show($"Folder does not exist yet:\n{folder}", "Folder Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (DirFolderNotConfiguredException ex)
        {
            Logger.Instance.Warning($"PartDetailControl: DIR folder not configured for '{ex.CustomerName}'");
            MessageBox.Show(
                $"No DIR folder configured for customer '{ex.CustomerName}'.\n\n" +
                $"Please open config.txt and set the folder path under [DirFolderMappings]:\n" +
                $"  {ex.CustomerName}=\n\n" +
                $"Config file:\n{ex.ConfigFilePath}",
                "DIR Folder Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartDetailControl: failed to open DIR folder: {ex.Message}");
            MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewDirButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mpContext == null) return;

        if (!DirFileService.TemplateExists())
        {
            MessageBox.Show($"DIR template not found:\n{DirFileService.TemplatePath}", "Template Not Found",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string folder;
        try
        {
            folder = DirFileService.ResolveFolder(_mpContext);
        }
        catch (DirFolderNotConfiguredException ex)
        {
            Logger.Instance.Warning($"PartDetailControl: DIR folder not configured for '{ex.CustomerName}'");
            MessageBox.Show(
                $"No DIR folder configured for customer '{ex.CustomerName}'.\n\n" +
                $"Please open config.txt and set the folder path under [DirFolderMappings]:\n" +
                $"  {ex.CustomerName}=\n\n" +
                $"Config file:\n{ex.ConfigFilePath}",
                "DIR Folder Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(folder))
        {
            var choice = MessageBox.Show(
                $"Folder does not exist yet:\n{folder}\n\nCreate it now?",
                "Folder Not Found", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;
            Directory.CreateDirectory(folder);
        }

        try
        {
            DirFileService.CreateAndOpen(_mpContext, folder);
            LoadDirSection();
        }
        catch (InvalidOperationException ex)
        {
            Logger.Instance.Warning($"PartDetailControl: DIR file already exists: {ex.Message}");
            MessageBox.Show(ex.Message, "File Already Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartDetailControl: failed to create DIR file: {ex.Message}");
            MessageBox.Show($"Failed to create DIR file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Bubble Drawing ───────────────────────────────────────────────────

    private static readonly int[] BubbleColumnWidths = { 300, 60, 140, 28 };

    private void LoadBubbleSection()
    {
        BubbleFilesPanel.Children.Clear();
        AssociateBubbleButton.Visibility = _mpContext != null ? Visibility.Visible : Visibility.Collapsed;
        if (_header == null) return;

        var attachments = _partRepository.GetBubbleAttachmentsByDrawingNumber(_header.DrawingNumber);
        if (attachments.Count == 0)
        {
            BubbleFilesPanel.Children.Add(new TextBox
            {
                Text = "(no bubble drawing files yet)", Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        BubbleFilesPanel.Children.Add(BuildBubbleFilesHeader());
        foreach (var attachment in attachments)
            BubbleFilesPanel.Children.Add(BuildBubbleFileRow(attachment));
    }

    private Grid BuildBubbleFilesHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in BubbleColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var headers = new[] { "File Name", "Rev", "Created at", "" };
        for (int i = 0; i < headers.Length; i++)
        {
            var block = new TextBox
            {
                Text = headers[i], Style = (Style)FindResource("SelectableText"),
                FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
            };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }
        return grid;
    }

    private Grid BuildBubbleFileRow(BubbleAttachmentRow attachment)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in BubbleColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var nameText = new TextBox
        {
            Text = attachment.FileName, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = attachment.FilePath
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        var revText = new TextBox
        {
            Text = ExtractRevisionFromFileName(attachment.FileName), Style = (Style)FindResource("SelectableText"),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(revText, 1);
        grid.Children.Add(revText);

        var createdText = new TextBox
        {
            Text = attachment.CreatedAt, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(createdText, 2);
        grid.Children.Add(createdText);

        var openBtn = new Button
        {
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(2, 0, 2, 0), ToolTip = "Open",
            Content = new Path
            {
                Data = (Geometry)FindResource("OpenInNewGeo"), Stretch = Stretch.Uniform,
                Fill = Brushes.DodgerBlue, Width = 13, Height = 13
            }
        };
        openBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        openBtn.Click += (_, _) => OpenBubbleFile(attachment);
        Grid.SetColumn(openBtn, 3);
        grid.Children.Add(openBtn);

        return grid;
    }

    private void OpenBubbleFile(BubbleAttachmentRow attachment)
    {
        if (!File.Exists(attachment.FilePath))
        {
            var choice = MessageBox.Show(
                $"File not found:\n{attachment.FilePath}\n\nRemove this entry from the database?",
                "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                _partRepository.RemoveBubbleAttachment(attachment.AttachmentId);
                LoadBubbleSection();
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

    private static string ExtractRevisionFromFileName(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"Rev([^\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Copies the current drawing number to the clipboard.
    /// </summary>
    /// <param name="sender">Copy button</param>
    /// <param name="e">Routed event args</param>
    private void CopyDrawingNumberButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_header?.DrawingNumber)) return;
        System.Windows.Clipboard.SetText(_header.DrawingNumber);
    }

    private void CopyBubbleNameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_header == null) return;
        var text = $"{_header.DrawingNumber} Rev{_header.Revision} {_header.Description}";
        System.Windows.Clipboard.SetText(text);
    }

    private void AssociateBubbleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mpContext == null || _header == null) return;

        var folder = BubbleConfig.GetBubbleFolder(_mpContext.CustomerName ?? string.Empty);
        var folderUsable = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);
        if (!folderUsable)
            Logger.Instance.Warning($"PartDetailControl.AssociateBubble: no usable bubble folder for customer '{_mpContext.CustomerName}'");

        var existing = _partRepository.GetBubbleAttachmentsByDrawingNumber(_header.DrawingNumber);
        var existingPaths = existing.Select(a => a.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        if (folderUsable)
        {
            var searchPattern = $"{_header.DrawingNumber} Rev* *-ballooned.pdf";
            var diskFiles = Directory.GetFiles(folder!, searchPattern);

            foreach (var filePath in diskFiles)
            {
                if (existingPaths.Contains(filePath)) continue;

                var fileName = System.IO.Path.GetFileName(filePath);
                var revision = ExtractRevisionFromFileName(fileName);
                var partId = _partRepository.GetPartIdByDrawingNumberAndRevision(_header.DrawingNumber, revision);
                if (partId == null)
                {
                    Logger.Instance.Warning($"PartDetailControl: skipped '{fileName}', no part record for drawing '{_header.DrawingNumber}' rev '{revision}'");
                    continue;
                }

                _partRepository.AddBubbleAttachment(partId.Value, fileName, filePath);
                added++;
            }
        }

        var missing = existing.Where(a => !File.Exists(a.FilePath)).ToList();
        foreach (var attachment in missing)
        {
            var choice = MessageBox.Show(
                $"File not found:\n{attachment.FilePath}\n\nRemove this entry from the database?",
                "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
                _partRepository.RemoveBubbleAttachment(attachment.AttachmentId);
        }

        // Nothing found automatically and nothing linked before: offer a manual pick.
        if (added == 0 && existing.Count == missing.Count)
        {
            var manualPath = PromptForFile(
                "Bubble Drawing Not Found",
                $"No bubble drawing was found automatically for drawing '{_header.DrawingNumber}'.\n\n" +
                "Select the bubble drawing manually?",
                "Select Bubble Drawing",
                "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                folderUsable ? folder : null);

            if (manualPath != null && !existingPaths.Contains(manualPath))
            {
                _partRepository.AddBubbleAttachment(_partId, System.IO.Path.GetFileName(manualPath), manualPath);
                added++;
            }
        }

        if (added > 0 || missing.Count > 0)
            LoadBubbleSection();
    }

    // ── Manual file association ─────────────────────────────────────────

    /// <summary>
    /// Asks whether to locate a file manually and, if confirmed, opens a file dialog.
    /// Shared by the MP, Bubble Drawing and Drawing PDF sections.
    /// </summary>
    /// <param name="promptTitle">Caption of the confirmation message box</param>
    /// <param name="promptMessage">Body of the confirmation message box</param>
    /// <param name="dialogTitle">Caption of the file dialog</param>
    /// <param name="filter">File dialog filter string</param>
    /// <param name="initialFolder">Folder to start browsing in; ignored when missing</param>
    /// <returns>The chosen file path, or null when the user declines or cancels</returns>
    private string? PromptForFile(
        string promptTitle, string promptMessage, string dialogTitle, string filter, string? initialFolder)
    {
        var choice = MessageBox.Show(promptMessage, promptTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return null;

        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title  = dialogTitle,
            Filter = filter
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
    }

    // ── Drawing PDF ──────────────────────────────────────────────────────

    /// <summary>
    /// Links a manually chosen PDF to this part, starting the file dialog in the folder of the
    /// PDF already on record. The chosen file becomes the part's active drawing file.
    /// </summary>
    /// <param name="sender">Associate button</param>
    /// <param name="e">Routed event args</param>
    private void AssociatePdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_header == null) return;

        var currentFiles = _partRepository.GetDrawingFiles(_partId);
        var initialFolder = currentFiles.Count > 0
            ? System.IO.Path.GetDirectoryName(currentFiles[0].FilePath)
            : null;

        var filePath = PromptForFile(
            "Associate Drawing PDF",
            $"Select the drawing PDF for '{_header.DrawingNumber}' manually?\n\n" +
            "The selected file becomes the active drawing file for this part.",
            "Select Drawing PDF",
            "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            initialFolder);
        if (filePath == null) return;

        var ok = new DrawingRepository().UpsertDrawingFile(
            _partId, System.IO.Path.GetFileName(filePath), filePath, _header.Revision ?? "-");

        if (!ok)
        {
            MessageBox.Show($"Failed to associate the PDF:\n{filePath}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Logger.Instance.Info($"PartDetailControl.AssociatePdf: linked '{filePath}' to partId={_partId}");
        LoadPdfFiles();
    }

    private void LoadPdfFiles()
    {
        PdfListPanel.Children.Clear();
        var files = _partRepository.GetDrawingFiles(_partId);
        if (files.Count == 0)
        {
            PdfListPanel.Children.Add(new TextBox
            {
                Text = "(no PDF files found)", Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        PdfListPanel.Children.Add(BuildPdfHeader());
        foreach (var file in files)
            PdfListPanel.Children.Add(BuildPdfRow(file));
    }

    private static readonly int[] PdfColumnWidths = { 260, 60, 70, 140, 28, 70 };

    private Grid BuildPdfHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in PdfColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        var headers = new[] { "File Name", "Rev", "Active", "Last Modified", "PDF", "Copy Path" };
        for (int i = 0; i < headers.Length; i++)
        {
            var block = new TextBox
            {
                Text = headers[i], Style = (Style)FindResource("SelectableText"),
                FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
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

        var nameText = new TextBox
        {
            Text = file.FileName, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = file.FilePath
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        var revisionText = new TextBox
        {
            Text = file.Revision, Style = (Style)FindResource("SelectableText"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(revisionText, 1);
        grid.Children.Add(revisionText);

        var activeText = new TextBox
        {
            Text = file.IsActive ? "Active" : string.Empty, Style = (Style)FindResource("SelectableText"),
            FontSize = 12, Foreground = Brushes.SeaGreen, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(activeText, 2);
        grid.Children.Add(activeText);

        var modifiedText = new TextBox
        {
            Text = file.LastModifiedAt ?? string.Empty, Style = (Style)FindResource("SelectableText"),
            FontSize = 12, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center
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

        var copyPathBtn = new Button
        {
            Style = (Style)FindResource("IconLinkBtn"), Padding = new Thickness(2, 0, 2, 0),
            ToolTip = "Copy folder path",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Content = new Path
            {
                Data = (Geometry)Resources["ContentCopyGeo"], Stretch = Stretch.Uniform,
                Fill = Brushes.DimGray, Width = 13, Height = 13
            }
        };
        copyPathBtn.Click += (_, _) => CopyPdfFolderPath(file.FilePath);
        Grid.SetColumn(copyPathBtn, 5);
        grid.Children.Add(copyPathBtn);

        return grid;
    }

    /// <summary>
    /// Copies the folder containing the given PDF to the clipboard.
    /// </summary>
    /// <param name="filePath">Full path of the drawing PDF</param>
    private void CopyPdfFolderPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var folder = System.IO.Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            Logger.Instance.Warning($"CopyPdfFolderPath: no folder resolved from '{filePath}'");
            return;
        }

        System.Windows.Clipboard.SetText(folder);
        Logger.Instance.Info($"CopyPdfFolderPath: copied '{folder}'");
    }

    // ── Process Template ────────────────────────────────────────────────

    /// <summary>
    /// Looks for the MP file of this order item and imports it. When no file is found automatically,
    /// offers to let the user pick one manually, starting in the customer's MP folder.
    /// </summary>
    /// <param name="sender">Associate button</param>
    /// <param name="e">Routed event args</param>
    private void AssociateMpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mpContext == null) return;

        string? filePath = null;
        try
        {
            var folder = MpFileService.ResolveFolder(_mpContext);
            // GetExistingFiles is ordered by last write time descending, so the newest file wins.
            filePath = MpFileService.GetExistingFiles(folder, _mpContext.DrawingNumber).FirstOrDefault();

            if (filePath == null)
                Logger.Instance.Info($"PartDetailControl.AssociateMp: no MP file for drawing '{_mpContext.DrawingNumber}' in '{folder}'");
        }
        catch (MpFolderNotConfiguredException ex)
        {
            Logger.Instance.Warning($"PartDetailControl.AssociateMp: no MP folder configured for '{ex.CustomerName}'");
        }

        filePath ??= PromptForMpFile();
        if (filePath == null) return;

        ImportMpFile(filePath);
    }

    /// <summary>
    /// Asks whether to pick the MP file manually and, if confirmed, opens a file dialog starting
    /// in the customer's configured MP folder.
    /// </summary>
    /// <returns>The chosen file path, or null when the user declines or cancels</returns>
    private string? PromptForMpFile()
    {
        string? initialFolder = null;
        try { initialFolder = MpFileService.ResolveCustomerFolder(_mpContext!); }
        catch (MpFolderNotConfiguredException ex)
        {
            Logger.Instance.Warning($"PartDetailControl.PromptForMpFile: no MP folder configured for '{ex.CustomerName}'");
        }

        return PromptForFile(
            "MP File Not Found",
            $"No MP file was found automatically for drawing '{_mpContext!.DrawingNumber}'.\n\n" +
            "Select the MP file manually?",
            "Select MP File",
            "MP Files (*.xlsm)|*.xlsm|Excel Files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All Files (*.*)|*.*",
            initialFolder);
    }

    /// <summary>
    /// Previews the MP file and, on confirmation, imports its process steps and links the file
    /// to the current part / order item.
    /// </summary>
    /// <param name="filePath">Full path of the MP file to import</param>
    private void ImportMpFile(string filePath)
    {
        MpExtractionResult result;
        try
        {
            result = MpExtractorService.Extract(filePath);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartDetailControl.ImportMpFile: failed to read '{filePath}': {ex.Message}");
            MessageBox.Show($"Failed to read MP file:\n{filePath}\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var stepsWillBeSkipped = _partRepository.HasProcessSteps(_partId);
        var dialog = new MpAssociateDialog(result, stepsWillBeSkipped)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        if (stepsWillBeSkipped)
            Logger.Instance.Warning($"PartDetailControl.ImportMpFile: partId={_partId} already has process templates - step import skipped");
        else if (result.ProcessSteps.Count > 0)
            _partRepository.AddProcessSteps(_partId, result.ProcessSteps);

        var alreadyLinked = _partRepository.GetMpAttachments(_partId)
            .Any(a => string.Equals(a.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (!alreadyLinked)
            _partRepository.AddMpAttachment(_partId, _orderItemId, result.FileName, filePath);

        LoadMpSection();
        LoadProcessSteps();
    }

    private void LoadProcessSteps()
    {
        ProcessStepsPanel.Children.Clear();
        AssociateMpButton.Visibility = _mpContext != null ? Visibility.Visible : Visibility.Collapsed;

        var steps = _partRepository.GetProcessSteps(_partId, _orderItemId);
        UpdateStepButton.Visibility = steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (steps.Count == 0)
        {
            ProcessStepsPanel.Children.Add(new TextBox
            {
                Text = "(no process template defined)", Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        var currentIndex = FindCurrentStepIndex(steps);
        ProcessStepsPanel.Children.Add(BuildProcessStepsHeader());
        for (int i = 0; i < steps.Count; i++)
            ProcessStepsPanel.Children.Add(BuildProcessStepRow(steps[i], i == currentIndex));
    }

    /// <summary>
    /// Finds the current step: the last step (in row order) that has a step_tracker record.
    /// SetCurrentStep removes trackers of later steps, so this is always the marked step.
    /// </summary>
    /// <param name="steps">Steps of the part, ordered by row number</param>
    /// <returns>Index into <paramref name="steps"/>, or -1 when nothing is tracked yet</returns>
    private static int FindCurrentStepIndex(List<ProcessStepRow> steps)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            var step = steps[i];
            if (step.Status != null || step.StartTime != null || step.EndTime != null)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Opens the current-step dialog and writes the chosen progress to the database.
    /// </summary>
    /// <param name="sender">Update button</param>
    /// <param name="e">Routed event args</param>
    private void UpdateStepButton_Click(object sender, RoutedEventArgs e)
    {
        var steps = _partRepository.GetProcessSteps(_partId, _orderItemId);
        if (steps.Count == 0) return;

        var currentIndex = FindCurrentStepIndex(steps);
        int? currentTemplateId = currentIndex >= 0 ? steps[currentIndex].ProcessTemplateId : null;

        var dialog = new CurrentStepDialog(steps, currentTemplateId)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        var templateIds = steps.Select(s => s.ProcessTemplateId).ToList();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_partRepository.SetCurrentStep(_orderItemId, templateIds, dialog.SelectedProcessTemplateId, today))
        {
            MessageBox.Show("Failed to update the current step. See the log for details.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadProcessSteps();
    }

    private Grid BuildProcessStepsHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var width in StepColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

        void AddHeader(int col, string text)
        {
            var block = new TextBox
            {
                Text = text, Style = (Style)FindResource("SelectableText"),
                FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray
            };
            Grid.SetColumn(block, col);
            grid.Children.Add(block);
        }

        var headers = new[] { "", "Row", "Shop Code", "Description", "Remark", "Operator", "Machine", "Status", "Start", "End" };
        for (int i = 0; i < headers.Length; i++)
            AddHeader(i, headers[i]);

        return grid;
    }

    private static readonly GridLength[] StepColumnWidths =
    {
        new GridLength(75), // Current-step marker
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

    /// <summary>
    /// Builds one process step row, optionally prefixed with the current-step marker.
    /// </summary>
    /// <param name="step">Step to render</param>
    /// <param name="isCurrent">True when this step is the current progress of the order item</param>
    private Grid BuildProcessStepRow(ProcessStepRow step, bool isCurrent)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        foreach (var width in StepColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

        if (isCurrent)
        {
            var marker = BuildCurrentStepMarker();
            Grid.SetColumn(marker, 0);
            grid.Children.Add(marker);
        }

        var values = new[]
        {
            step.RowNumber.ToString(), step.ShopCode, FlattenText(step.Description),
            step.Remark ?? string.Empty, step.OperatorId ?? string.Empty, step.MachineId ?? string.Empty,
            step.Status ?? string.Empty, step.StartTime ?? string.Empty, step.EndTime ?? string.Empty
        };

        for (int i = 0; i < values.Length; i++)
        {
            var block = new TextBox
            {
                Text = values[i], Style = (Style)FindResource("SelectableText"),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, i + 1);
            grid.Children.Add(block);
        }

        return grid;
    }

    /// <summary>
    /// Builds the "Current →" marker shown in the first column of the current step's row.
    /// </summary>
    private StackPanel BuildCurrentStepMarker()
    {
        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Current", FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = Brushes.Red, VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "→", FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brushes.Red, Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    /// <summary>
    /// Collapses line breaks into single spaces so multi-line MP descriptions stay on one row.
    /// </summary>
    /// <param name="text">Raw text, possibly containing CR/LF</param>
    /// <returns>Single-line text; empty string when the input is null</returns>
    private static string FlattenText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    // ── Notes ────────────────────────────────────────────────────────────

    private void LoadNotes()
    {
        NotesListPanel.Children.Clear();
        var notes = _partRepository.GetPartNotes(_partId);
        if (notes.Count == 0)
        {
            NotesListPanel.Children.Add(new TextBox
            {
                Text = "(no notes yet)", Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        foreach (var note in notes)
        {
            NotesListPanel.Children.Add(new TextBox
            {
                Text = $"[{note.CreatedAt}] {note.Author ?? "unknown"}: {note.Content}",
                Style = (Style)FindResource("SelectableText"),
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

    /// <summary>
    /// Reloads the whole page from the database using the current part / order item.
    /// </summary>
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info($"PartDetailControl: refresh requested for partId={_partId}");
        LoadPart(_partId, _orderItemId);
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
