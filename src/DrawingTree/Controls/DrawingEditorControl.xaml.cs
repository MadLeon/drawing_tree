using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using Microsoft.Win32;
using DrawingTree.Logging;
using DrawingTree.Models;

namespace DrawingTree.Controls;

/// <summary>
/// User control for editing drawing information
/// Allows users to add, delete, and modify drawing entries
/// </summary>
public partial class DrawingEditorControl : System.Windows.Controls.UserControl
{
    private ObservableCollection<DrawingInfo> _drawingList = new ObservableCollection<DrawingInfo>();

    /// <summary>
    /// Event raised when user clicks Return button
    /// </summary>
    public event EventHandler? ReturnRequested;

    public DrawingEditorControl()
    {
        InitializeComponent();
        DrawingListControl.DataContext = _drawingList;
    }

    /// <summary>
    /// Handle index text loaded event
    /// Set the index number for each row
    /// </summary>
    private void IndexText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBlock textBlock && textBlock.Tag is DrawingInfo drawing)
        {
            int index = _drawingList.IndexOf(drawing);
            textBlock.Text = $"{index + 1}.";
        }
    }

    /// <summary>
    /// Load drawing information list into the editor
    /// </summary>
    /// <param name="drawings">List of drawing information</param>
    public void LoadDrawings(System.Collections.Generic.List<DrawingInfo> drawings)
    {
        _drawingList.Clear();
        foreach (var drawing in drawings)
        {
            _drawingList.Add(drawing);
        }
        Logger.Instance.Info($"Loaded {drawings.Count} drawings into editor");

        // Check for duplicates after loading
        CheckAllDuplicates();

        // Update status display
        UpdateStatusDisplay();
    }

    /// <summary>
    /// Get current drawing list
    /// </summary>
    /// <returns>Current drawing information list</returns>
    public ObservableCollection<DrawingInfo> GetDrawings()
    {
        return _drawingList;
    }

    /// <summary>
    /// Update status display with current count
    /// </summary>
    private void UpdateStatusDisplay()
    {
        StatusText.Text = $"{_drawingList.Count} drawing(s) in list";
    }

    /// <summary>
    /// Check all drawings for duplicates
    /// </summary>
    private void CheckAllDuplicates()
    {
        // Reset all duplicate flags
        foreach (var drawing in _drawingList)
        {
            drawing.HasDuplicate = false;
        }

        // Build hash sets to track duplicates
        var drawingNumberCounts = new Dictionary<string, int>();
        var pathCounts = new Dictionary<string, int>();

        foreach (var drawing in _drawingList)
        {
            if (!string.IsNullOrWhiteSpace(drawing.DrawingNumber))
            {
                if (!drawingNumberCounts.ContainsKey(drawing.DrawingNumber))
                    drawingNumberCounts[drawing.DrawingNumber] = 0;
                drawingNumberCounts[drawing.DrawingNumber]++;
            }

            if (!string.IsNullOrWhiteSpace(drawing.PdfPath))
            {
                if (!pathCounts.ContainsKey(drawing.PdfPath))
                    pathCounts[drawing.PdfPath] = 0;
                pathCounts[drawing.PdfPath]++;
            }
        }

        // Mark duplicates
        foreach (var drawing in _drawingList)
        {
            bool hasDuplicateNumber = !string.IsNullOrWhiteSpace(drawing.DrawingNumber) && 
                                      drawingNumberCounts.ContainsKey(drawing.DrawingNumber) && 
                                      drawingNumberCounts[drawing.DrawingNumber] > 1;

            bool hasDuplicatePath = !string.IsNullOrWhiteSpace(drawing.PdfPath) && 
                                    pathCounts.ContainsKey(drawing.PdfPath) && 
                                    pathCounts[drawing.PdfPath] > 1;

            drawing.HasDuplicate = hasDuplicateNumber || hasDuplicatePath;
        }
    }

    /// <summary>
    /// Check if there are any duplicates in the list
    /// </summary>
    /// <returns>True if duplicates exist</returns>
    private bool HasDuplicates()
    {
        return _drawingList.Any(d => d.HasDuplicate);
    }

    /// <summary>
    /// Handle Add Drawing button click
    /// Add a new empty drawing entry to the list
    /// </summary>
    private void AddDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        var newDrawing = new DrawingInfo
        {
            DrawingNumber = string.Empty,
            PdfPath = string.Empty
        };
        _drawingList.Add(newDrawing);
        Logger.Instance.Info("Added new empty drawing entry");

        // Update status
        UpdateStatusDisplay();
    }

    /// <summary>
    /// Handle drawing number text changed event
    /// Check for duplicates when drawing number is modified
    /// </summary>
    private void DrawingNumber_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox && textBox.Tag is DrawingInfo)
        {
            CheckAllDuplicates();
        }
    }

    /// <summary>
    /// Handle Delete button click
    /// Show confirmation dialog before deleting the drawing entry
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is DrawingInfo drawing)
        {
            // Show confirmation dialog
            System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete drawing: {drawing.DrawingNumber}?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _drawingList.Remove(drawing);
                Logger.Instance.Info($"Deleted drawing: {drawing.DrawingNumber}");

                // Recheck duplicates after deletion
                CheckAllDuplicates();

                // Update status
                UpdateStatusDisplay();
            }
        }
    }

    /// <summary>
    /// Handle Select File button click
    /// Show file selection dialog to update PDF path
    /// </summary>
    private void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is DrawingInfo drawing)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select PDF File",
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                drawing.PdfPath = openFileDialog.FileName;
                Logger.Instance.Info($"Updated PDF path for drawing {drawing.DrawingNumber}: {drawing.PdfPath}");

                // Check for duplicates after path change
                CheckAllDuplicates();
            }
        }
    }

    /// <summary>
    /// Handle Confirm button click
    /// Validate Purchase Order, check for duplicates, and export to JSON file
    /// </summary>
    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if Purchase Order is filled
        string purchaseOrder = PurchaseOrderTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(purchaseOrder))
        {
            System.Windows.MessageBox.Show(
                "Please enter a Purchase Order number before confirming.",
                "Purchase Order Required",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            Logger.Instance.Warning("Confirm rejected: Purchase Order is empty");
            PurchaseOrderTextBox.Focus();
            return;
        }

        // Check for duplicates before confirming
        if (HasDuplicates())
        {
            System.Windows.MessageBox.Show(
                "Cannot confirm: There are duplicate drawing numbers or file paths.\nPlease fix the highlighted entries (shown with red borders).",
                "Duplicate Entries Detected",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            Logger.Instance.Warning("Confirm rejected due to duplicate entries");
            return;
        }

        // Export to JSON
        try
        {
            string exportedFilePath = ExportToJson(purchaseOrder);

            System.Windows.MessageBox.Show(
                $"Drawing information has been saved to:\n{exportedFilePath}",
                "Confirmed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            Logger.Instance.Info("Drawing information confirmed and exported successfully");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to export drawing information: {ex.Message}",
                "Export Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Logger.Instance.Error($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Export drawing information to JSON file
    /// </summary>
    /// <param name="purchaseOrder">Purchase Order number</param>
    /// <returns>Full path to the exported JSON file</returns>
    private string ExportToJson(string purchaseOrder)
    {
        // Create data structure for JSON export
        var exportData = new
        {
            PurchaseOrder = purchaseOrder,
            ExportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalDrawings = _drawingList.Count,
            Drawings = _drawingList.Select(d => new
            {
                DrawingNumber = d.DrawingNumber,
                PdfPath = d.PdfPath,
                FileName = d.FileName
            }).ToList()
        };

        // Serialize to JSON with formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        string jsonString = JsonSerializer.Serialize(exportData, options);

        // Create filename with uppercase Purchase Order
        string fileName = $"{purchaseOrder.ToUpper()}.json";

        // Get application directory and create full path
        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string fullPath = Path.Combine(appDirectory, fileName);

        // Write to file
        File.WriteAllText(fullPath, jsonString);

        Logger.Instance.Info($"Exported {_drawingList.Count} drawings to {fullPath}");
        Logger.Instance.Info("=== Drawing Information Confirmed ===");
        Logger.Instance.Info($"Purchase Order: {purchaseOrder}");
        Logger.Instance.Info($"Total drawings: {_drawingList.Count}");

        foreach (var drawing in _drawingList)
        {
            Logger.Instance.Info($"Drawing Number: {drawing.DrawingNumber}, Path: {drawing.PdfPath}");
        }

        Logger.Instance.Info("=== End of Drawing Information ===");

        return fullPath;
    }

    /// <summary>
    /// Handle Return button click
    /// Clear the drawing list and raise ReturnRequested event
    /// </summary>
    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        // Check if there are any drawings in the list
        if (_drawingList.Count > 0)
        {
            System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                "You have unsaved changes. Are you sure you want to return?",
                "Confirm Return",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }
        }

        _drawingList.Clear();
        Logger.Instance.Info("Drawing editor cleared, returning to main view");

        // Raise ReturnRequested event
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }
}
