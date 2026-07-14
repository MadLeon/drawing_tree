/// <summary>
/// OeAnomalyEditDialog.xaml.cs
/// Generic edit-and-save form for an Anomaly row in the OE sync review dialog (Issue #44
/// follow-up). Every relevant field is shown as an editable textbox regardless of why the row was
/// flagged as an anomaly, so the reviewer can hand-correct the data (or mark the item as
/// inactive/shipped) from one place instead of relying on the diff engine to guess.
/// </summary>

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Models;
using DrawingTree.Services;

using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DrawingTree.Dialogs;

public partial class OeAnomalyEditDialog : Window
{
    private readonly OeSyncRepository _syncRepository;
    private readonly PoRepository _poRepository = new();
    private readonly OeSyncChange _change;
    private List<CustomerRow> _customers = new();

    /// <summary>True when the dialog closed via the "Mark as inactive/shipped" path rather than a
    /// field edit — the caller uses this to decide whether to record a "corrected" memory entry.</summary>
    public bool MarkedShipped { get; private set; }

    public OeAnomalyEditDialog(OeSyncRepository syncRepository, OeSyncChange change)
    {
        _syncRepository = syncRepository;
        _change = change;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleBlock.Text = _change.HeaderText;

        _customers = _poRepository.GetAllCustomers();
        CustomerBox.ItemsSource = _customers.Select(c => c.Name).ToList();

        // Prefill from the Excel row whenever one exists — it's the target value the reviewer is
        // being asked to accept (e.g. a "job/M already exists archived, N fields differ" Anomaly:
        // the form should show the NEW value Excel proposes, not the OLD DB value, so accepting it
        // is just "click Update" instead of retyping every changed field by hand). Only fall back to
        // the DB row when there's no Excel row to propose anything (e.g. "sibling rows missing from
        // the OE file" — DbRow only, nothing to prefer over the current DB value).
        if (_change.ExcelRow != null)
        {
            var row = _change.ExcelRow;
            JobNumberBox.Text = row.JobNumber;
            CustomerBox.Text = row.Customer;
            LoadContacts(row.Customer);
            ContactBox.Text = row.Contact;
            // Resolved, not raw: some OE rows type the literal word "NPO" into the P.O. column
            // instead of leaving it blank (e.g. Job #72113/#72942) — show the placeholder the app
            // would actually write ("NPO-{job}"), not that literal text.
            PoNumberBox.Text = OeNormalization.ResolvePoNumber(row.PoNumber, row.JobNumber);
            OeNumberBox.Text = row.OeNumber;
            DrawingBox.Text = row.PartNumber;
            RevBox.Text = row.Revision;
            DescBox.Text = row.Description;
            QtyBox.Text = row.Quantity;
            LnBox.Text = row.LineNumber;
            PriceBox.Text = row.Price?.ToString() ?? "";
            DwgReleasePicker.Text = row.DrawingReleaseDate;
            DelPicker.Text = row.DeliveryRequiredDate;
        }
        else if (_change.DbRow != null)
        {
            var db = _change.DbRow;
            JobNumberBox.Text = db.JobNumber;
            CustomerBox.Text = db.CustomerName;
            LoadContacts(db.CustomerName);
            ContactBox.Text = db.ContactName;
            PoNumberBox.Text = db.PoNumber;
            OeNumberBox.Text = db.OeNumber ?? "";
            DrawingBox.Text = db.DrawingNumber;
            RevBox.Text = db.Revision;
            DescBox.Text = db.Description ?? "";
            QtyBox.Text = db.Quantity;
            LnBox.Text = db.LineNumber;
            PriceBox.Text = db.ActualPrice?.ToString() ?? "";
            DwgReleasePicker.Text = db.DrawingReleaseDate ?? "";
            DelPicker.Text = db.DeliveryRequiredDate ?? "";
        }

        // "Mark as inactive/shipped" only makes sense when there is an existing order_item to
        // deactivate — a pure Add-type anomaly has nothing in the DB yet to mark shipped.
        ShipCheckBox.Visibility = _change.DbRow != null ? Visibility.Visible : Visibility.Collapsed;

        // Job # is the join key for an existing order_item and ApplyModify never reassigns it
        // (only ApplyAdd's insert path uses it) — disable editing here so the field can't imply a
        // change that would silently not be saved.
        JobNumberBox.IsEnabled = _change.DbRow == null;
    }

    private void CustomerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadContacts(CustomerBox.Text);

    private void LoadContacts(string? customerName)
    {
        var customer = _customers.FirstOrDefault(c => c.Name == customerName);
        if (customer == null)
        {
            ContactBox.ItemsSource = null;
            return;
        }
        var contacts = _poRepository.GetContactsByCustomer(customer.Id);
        ContactBox.ItemsSource = contacts.Select(c => c.Name).ToList();
    }

    private void ShipCheckBox_Click(object sender, RoutedEventArgs e)
        => FormGrid.IsEnabled = ShipCheckBox.IsChecked != true;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShipCheckBox.IsChecked == true)
        {
            SaveAsShipped();
            return;
        }

        SaveAsEdit();
    }

    private void SaveAsShipped()
    {
        if (MessageBox.Show("Mark this item as inactive/shipped?", "Confirm", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var (success, error) = _syncRepository.ApplyAnomaly(_change);
        if (!success)
        {
            MessageBox.Show($"Failed to mark as shipped:\n{error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"OeAnomalyEditDialog: ApplyAnomaly failed for order_item id={_change.OrderItemId}: {error}");
            return;
        }

        MarkedShipped = true;
        Logger.Instance.Info($"OeAnomalyEditDialog: marked order_item id={_change.OrderItemId} inactive (manual resolution)");
        DialogResult = true;
    }

    private void SaveAsEdit()
    {
        var formRow = new OeExcelRow
        {
            JobNumber = JobNumberBox.Text.Trim(),
            Customer = CustomerBox.Text.Trim(),
            Contact = ContactBox.Text.Trim(),
            PoNumber = PoNumberBox.Text.Trim(),
            OeNumber = OeNumberBox.Text.Trim(),
            PartNumber = DrawingBox.Text.Trim(),
            Revision = RevBox.Text.Trim(),
            Description = DescBox.Text.Trim(),
            Quantity = QtyBox.Text.Trim(),
            LineNumber = LnBox.Text.Trim(),
            Price = decimal.TryParse(PriceBox.Text.Trim(), out var price) ? price : null,
            DrawingReleaseDate = FormatDate(DwgReleasePicker.Text),
            DeliveryRequiredDate = FormatDate(DelPicker.Text),
        };

        if (string.IsNullOrWhiteSpace(formRow.LineNumber) || string.IsNullOrWhiteSpace(formRow.Quantity))
        {
            MessageBox.Show("M / Line # and Qty are required.", "Missing Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show("Save these changes?", "Confirm Edit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        (bool Success, string? Error) result;
        // Seeded from the diff-time NeedsItemReactivation/NeedsPoReactivation flags — set when this
        // Anomaly's DbRow was already known to be an inactive/archived "already exists" collision
        // (see OeSyncService.TryAddAdd) whose other fields also differ, so it arrives here with
        // DbRow pre-populated and the lookup below is skipped. Both default false for every other
        // Anomaly kind.
        bool reactivateItem = _change.NeedsItemReactivation, reactivatePo = _change.NeedsPoReactivation;

        if (_change.DbRow == null)
        {
            // This Anomaly was detected with no DbRow because GetActiveSnapshot only returns
            // active rows — but an inactive (historical/archived) order_item for this exact
            // (job_number, line_number) may still exist (e.g. the reviewer typed a line number for
            // a blank-M anomaly that happens to collide with one). Inserting blindly would violate
            // UNIQUE(order_item.job_id, order_item.line_number); reactivate-and-update it instead.
            var existing = _syncRepository.FindOrderItemAnyStatus(formRow.JobNumber, formRow.LineNumber);
            if (existing != null)
            {
                _change.DbRow = existing.Value.Row;
                _change.OrderItemId = existing.Value.Row.OrderItemId;
                _change.PoId = existing.Value.Row.PoId;
                reactivateItem = existing.Value.ItemWasInactive;
                reactivatePo = existing.Value.PoWasInactive;
            }
        }

        if (_change.DbRow == null)
        {
            _change.ExcelRow = formRow;
            var addResult = _syncRepository.ApplyAdd(_change);
            result = (addResult.Success, addResult.Error);
            if (addResult.Success) _change.CreatedOrderItemId = addResult.NewOrderItemId;
        }
        else
        {
            var diffs = OeSyncService.BuildNonQuantityFieldChanges(formRow, _change.DbRow);

            var (formQty, _) = OeNormalization.ResolveQuantity(formRow.Quantity);
            var (dbQty, _) = OeNormalization.ResolveQuantity(_change.DbRow.Quantity);
            if (formQty != dbQty)
                diffs.Add(new OeFieldChange { FieldLabel = "Qty.:", OldValue = dbQty, NewValue = formQty });

            if (OeNormalization.NormalizeLineNumber(formRow.LineNumber) != OeNormalization.NormalizeLineNumber(_change.DbRow.LineNumber))
                diffs.Add(new OeFieldChange { FieldLabel = "M (line #):", OldValue = _change.DbRow.LineNumber, NewValue = formRow.LineNumber });

            _change.FieldChanges.Clear();
            _change.FieldChanges.AddRange(diffs);
            _change.ExcelRow = formRow;
            result = _syncRepository.ApplyModify(_change, reactivateItem, reactivatePo);
        }

        if (!result.Success)
        {
            MessageBox.Show($"Save failed:\n{result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"OeAnomalyEditDialog: save failed: {result.Error}");
            return;
        }

        Logger.Instance.Info($"OeAnomalyEditDialog: saved edit for {formRow}");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private static string FormatDate(string rawText)
    {
        string trimmed = rawText.Trim();
        return DateTime.TryParse(trimmed, out var date) ? date.ToString("yyyy-MM-dd") : trimmed;
    }
}
