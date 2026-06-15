using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Data;
using DrawingTree.Dialogs;
using DrawingTree.Logging;
using DrawingTree.Models;

using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace DrawingTree.Controls;

/// <summary>
/// PartEditorControl.xaml.cs
/// Bulk editor for part metadata. Loads all drawings from a PO import file,
/// fetches current DB values for each, and allows per-row save with diff confirmation.
/// </summary>
public partial class PartEditorControl : UserControl
{
    private readonly DrawingRepository _drawingRepository = new();
    private readonly ObservableCollection<PartEditorRow> _rows = new();
    private string _poName = string.Empty;

    public event EventHandler? ReturnRequested;

    public PartEditorControl()
    {
        InitializeComponent();
        PartList.ItemsSource = _rows;
    }

    /// <summary>
    /// Parse the import JSON file and load each drawing's DB metadata into the row list.
    /// </summary>
    public void LoadFromJsonFile(string filePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        _poName = baseName.EndsWith("_import", StringComparison.OrdinalIgnoreCase)
            ? baseName[..^"_import".Length]
            : baseName;

        PoLabel.Text = $"PO: {_poName}";
        _rows.Clear();

        try
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Drawings", out var drawings)) return;

            int index = 1;
            foreach (var d in drawings.EnumerateArray())
            {
                string drawingNumber = d.GetProperty("DrawingNumber").GetString() ?? string.Empty;
                string pdfPath      = d.GetProperty("PdfPath").GetString() ?? string.Empty;

                DrawingInfo? dbInfo = _drawingRepository.GetDrawingInfo(drawingNumber);

                string qty = dbInfo?.PartId.HasValue == true
                    ? _drawingRepository.GetPartQuantity(dbInfo.PartId.Value)
                    : string.Empty;

                _rows.Add(new PartEditorRow
                {
                    Index              = index++,
                    PartId             = dbInfo?.PartId,
                    DrawingNumber      = drawingNumber,
                    Revision           = dbInfo?.Revision    ?? string.Empty,
                    Description        = dbInfo?.Description ?? string.Empty,
                    IsAssembly         = dbInfo?.IsAssembly  ?? false,
                    PdfPath            = dbInfo?.PdfPath.Length > 0 ? dbInfo.PdfPath : pdfPath,
                    QuantityInAssembly = qty
                });
            }

            Logger.Instance.Info($"PartEditor loaded {_rows.Count} rows for PO: {_poName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load import file: {ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"PartEditor load failed for '{filePath}': {ex.Message}");
        }
    }

    // ── Per-row save ──────────────────────────────────────────────────────

    private void SaveRow(PartEditorRow row)
    {
        if (row.PartId == null)
        {
            row.Status = SaveStatus.Error;
            Logger.Instance.Warning($"PartEditor save skipped: '{row.DrawingNumber}' has no DB record");
            Snackbar.Show($"Not in database: {row.DrawingNumber}");
            return;
        }

        DrawingInfo? dbInfo = _drawingRepository.GetDrawingInfo(row.DrawingNumber);
        if (dbInfo == null)
        {
            row.Status = SaveStatus.Error;
            Logger.Instance.Warning($"PartEditor save: GetDrawingInfo returned null for '{row.DrawingNumber}'");
            return;
        }

        string dbQty = _drawingRepository.GetPartQuantity(row.PartId.Value);

        bool same = row.Revision           == dbInfo.Revision    &&
                    row.Description        == dbInfo.Description  &&
                    row.IsAssembly         == dbInfo.IsAssembly   &&
                    row.PdfPath            == dbInfo.PdfPath      &&
                    row.QuantityInAssembly == dbQty;

        if (same)
        {
            row.Status = SaveStatus.Success;
            return;
        }

        var dialog = new ConfirmOverwriteDialog(row, dbInfo, dbQty) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        bool ok = _drawingRepository.UpdatePart(
            row.PartId.Value, row.Revision, row.Description, row.IsAssembly);

        if (ok && !string.IsNullOrEmpty(row.PdfPath))
        {
            ok = _drawingRepository.UpsertDrawingFile(
                row.PartId.Value,
                Path.GetFileName(row.PdfPath),
                row.PdfPath,
                row.Revision);
        }

        if (ok && !string.IsNullOrEmpty(row.QuantityInAssembly))
            ok = _drawingRepository.UpdatePartTreeQuantity(row.PartId.Value, row.QuantityInAssembly);

        row.Status = ok ? SaveStatus.Success : SaveStatus.Error;

        if (ok)
            Logger.Instance.Info($"PartEditor saved: {row.DrawingNumber} (partId={row.PartId})");
        else
            Logger.Instance.Error($"PartEditor save failed: {row.DrawingNumber}");
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void RowSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is PartEditorRow row)
            SaveRow(row);
    }

    private void SaveAllButton_Click(object sender, RoutedEventArgs e)
    {
        var pending = _rows.Where(r => r.Status != SaveStatus.Success).ToList();
        foreach (var row in pending)
            SaveRow(row);

        int saved = _rows.Count(r => r.Status == SaveStatus.Success);
        Snackbar.Show($"Saved {saved} / {_rows.Count} parts");
        Logger.Instance.Info($"PartEditor Save All: {saved}/{_rows.Count} succeeded for PO: {_poName}");
    }

    private void RowBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not PartEditorRow row) return;

        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title  = "Select PDF File",
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            row.PdfPath = dialog.FileName;
    }

    private void RowOpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is PartEditorRow row)
            OpenPdf(row.PdfPath);
    }

    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("Returning from part editor");
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

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
