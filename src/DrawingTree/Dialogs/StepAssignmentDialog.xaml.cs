/// <summary>
/// StepAssignmentDialog.xaml.cs
/// Modal dialog for assigning a process step tracker entry to an order item.
/// Lets the user choose a process template step, enter a start date (required),
/// and optionally enter an end date.
/// </summary>

using System.Windows;
using DrawingTree.Data;

using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

namespace DrawingTree.Dialogs;

public partial class StepAssignmentDialog : Window
{
    /// <summary>Result set when the user clicks OK. Null means the dialog was cancelled.</summary>
    public StepAssignmentResult? Result { get; private set; }

    public StepAssignmentDialog(List<ProcessTemplateStep> steps,
                                 DateTime? initialStart = null,
                                 DateTime? initialEnd   = null)
    {
        InitializeComponent();

        var items = steps.Select(s => new StepComboItem(
            s.Id,
            s.Description != null
                ? $"Step {s.RowNumber}: {s.ShopCode} — {s.Description}"
                : $"Step {s.RowNumber}: {s.ShopCode}")).ToList();

        StepComboBox.ItemsSource = items;
        if (items.Count > 0) StepComboBox.SelectedIndex = 0;

        if (initialStart.HasValue) StartDatePicker.SelectedDate = initialStart;
        if (initialEnd.HasValue)   EndDatePicker.SelectedDate   = initialEnd;
    }

    /// <summary>
    /// Pre-selects the step matching the given process_template_id.
    /// Call after construction to pre-populate an edit scenario.
    /// </summary>
    public void PreSelectStep(int processTemplateId)
    {
        for (int i = 0; i < StepComboBox.Items.Count; i++)
        {
            if (StepComboBox.Items[i] is StepComboItem item && item.Id == processTemplateId)
            {
                StepComboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (StepComboBox.SelectedItem is not StepComboItem selectedStep)
        {
            MessageBox.Show("Please select a step.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (StartDatePicker.SelectedDate == null)
        {
            MessageBox.Show("Start date is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new StepAssignmentResult(
            selectedStep.Id,
            StartDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd"),
            EndDatePicker.SelectedDate?.ToString("yyyy-MM-dd"));

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private record StepComboItem(int Id, string DisplayText);
}

/// <param name="ProcessTemplateId">process_template.id chosen by the user</param>
/// <param name="StartDate">ISO date string (yyyy-MM-dd)</param>
/// <param name="EndDate">ISO date string or null</param>
public record StepAssignmentResult(int ProcessTemplateId, string StartDate, string? EndDate);
