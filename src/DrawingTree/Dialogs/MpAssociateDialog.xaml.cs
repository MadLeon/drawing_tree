/// <summary>
/// MpAssociateDialog.xaml.cs
/// Read-only preview of the data extracted from an MP workbook, shown before the steps
/// are imported into process_template and the file is linked to the order item.
/// </summary>

using System.Windows;
using System.Windows.Controls;
using DrawingTree.Models;

using Window   = System.Windows.Window;
using TextBox  = System.Windows.Controls.TextBox;
using Brushes  = System.Windows.Media.Brushes;

namespace DrawingTree.Dialogs;

public partial class MpAssociateDialog : Window
{
    private static readonly GridLength[] StepColumnWidths =
    {
        new GridLength(50),
        new GridLength(90),
        new GridLength(1, GridUnitType.Star)
    };

    /// <summary>
    /// Builds the dialog from an extraction result.
    /// </summary>
    /// <param name="result">Data extracted from the MP workbook</param>
    /// <param name="stepsWillBeSkipped">True when the part already has a process template,
    /// so only the file link will be created</param>
    public MpAssociateDialog(MpExtractionResult result, bool stepsWillBeSkipped)
    {
        InitializeComponent();

        FileNameText.Text = result.FileName;
        FilePathText.Text = result.FilePath;

        PoNumberText.Text      = result.PoNumber;
        OeNumberText.Text      = result.OeNumber;
        JobNumberText.Text     = result.JobNumber;
        LineNumberText.Text    = result.LineNumber;
        DrawingNumberText.Text = result.DrawingNumber;
        RevisionText.Text      = result.Revision;
        ReleaseDateText.Text   = result.DrawingReleaseDate;
        DeliveryDateText.Text  = result.DeliveryRequiredDate;
        DescriptionText.Text   = result.Description;
        QuantityText.Text      = result.Quantity;

        if (stepsWillBeSkipped)
            SkipNoticeText.Visibility = Visibility.Visible;

        LoadSteps(result.ProcessSteps);
    }

    /// <summary>
    /// Renders the process steps as a read-only header row plus one row per step.
    /// </summary>
    /// <param name="steps">Steps extracted from the workbook</param>
    private void LoadSteps(List<MpProcessStep> steps)
    {
        if (steps.Count == 0)
        {
            StepsPanel.Children.Add(new TextBox
            {
                Text = "(no process steps found in this file)",
                Style = (Style)FindResource("SelectableText"),
                FontSize = 12, Foreground = Brushes.Gray
            });
            return;
        }

        StepsPanel.Children.Add(BuildRow(
            new[] { "Row", "Shop Code", "Description" }, isHeader: true));

        foreach (var step in steps)
            StepsPanel.Children.Add(BuildRow(
                new[] { step.RowNumber.ToString(), step.ShopCode, step.ProcessDescription }, isHeader: false));
    }

    /// <summary>
    /// Builds one grid row of read-only cells using the shared step column widths.
    /// </summary>
    /// <param name="values">Cell texts, one per column</param>
    /// <param name="isHeader">True to render as a gray semi-bold header row</param>
    /// <returns>The populated grid</returns>
    private Grid BuildRow(string[] values, bool isHeader)
    {
        var grid = new Grid { Margin = new Thickness(0, isHeader ? 0 : 1, 0, isHeader ? 4 : 1) };
        foreach (var width in StepColumnWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

        for (int i = 0; i < values.Length; i++)
        {
            var cell = new TextBox
            {
                Text = values[i],
                Style = (Style)FindResource("SelectableText"),
                FontSize = isHeader ? 11 : 12,
                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isHeader ? Brushes.Gray : Brushes.Black,
                TextWrapping = isHeader ? TextWrapping.NoWrap : TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private void AssociateButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
