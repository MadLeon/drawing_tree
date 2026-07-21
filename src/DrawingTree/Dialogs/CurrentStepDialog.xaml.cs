/// <summary>
/// CurrentStepDialog.xaml.cs
/// Modal dialog that lets the user pick which process step an order item is currently on.
/// Shows every step of the part in a dropdown and returns the chosen process_template.id.
/// </summary>
/// <remarks>
/// Usage:
/// - Pass the part's steps ordered by row number and the currently marked step (if any)
/// - Read SelectedProcessTemplateId after ShowDialog() returns true
/// </remarks>

using System.Windows;
using DrawingTree.Data;

using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

namespace DrawingTree.Dialogs;

public partial class CurrentStepDialog : Window
{
    /// <summary>process_template.id chosen by the user; only valid when DialogResult is true.</summary>
    public int SelectedProcessTemplateId { get; private set; }

    /// <summary>
    /// Builds the dialog from the part's process steps.
    /// </summary>
    /// <param name="steps">Steps of the part, ordered by row number</param>
    /// <param name="currentProcessTemplateId">process_template.id to pre-select; null selects the first step</param>
    public CurrentStepDialog(IReadOnlyList<ProcessStepRow> steps, int? currentProcessTemplateId)
    {
        InitializeComponent();

        var items = steps.Select(s => new StepComboItem(s.ProcessTemplateId, BuildDisplayText(s))).ToList();
        StepComboBox.ItemsSource = items;

        var index = currentProcessTemplateId.HasValue
            ? items.FindIndex(i => i.Id == currentProcessTemplateId.Value)
            : -1;
        StepComboBox.SelectedIndex = index >= 0 ? index : (items.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// Formats one step for the dropdown, collapsing line breaks in the description
    /// so every entry stays on a single line.
    /// </summary>
    /// <param name="step">Step to format</param>
    /// <returns>Display text such as "Step 3: WELD — Weld frame to base"</returns>
    private static string BuildDisplayText(ProcessStepRow step)
    {
        var description = step.Description?.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return string.IsNullOrEmpty(description)
            ? $"Step {step.RowNumber}: {step.ShopCode}"
            : $"Step {step.RowNumber}: {step.ShopCode} — {description}";
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (StepComboBox.SelectedItem is not StepComboItem selected)
        {
            MessageBox.Show("Please select a step.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedProcessTemplateId = selected.Id;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private record StepComboItem(int Id, string DisplayText);
}
