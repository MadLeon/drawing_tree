using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Models;

using MessageBox           = System.Windows.MessageBox;
using MessageBoxButton     = System.Windows.MessageBoxButton;
using MessageBoxImage      = System.Windows.MessageBoxImage;
using MessageBoxResult     = System.Windows.MessageBoxResult;

namespace DrawingTree.Dialogs;

/// <summary>
/// NewJobDialog.xaml.cs
/// Dialog for creating a new order_item cascade (customer → PO → job → part → order_item).
/// </summary>
public partial class NewJobDialog : Window
{
    private static readonly Regex PriceRegex = new(@"^[0-9]*\.?[0-9]{0,2}$");

    private readonly PoRepository _repository;
    private readonly DrawingRepository _drawingRepository = new();
    private List<CustomerRow> _customers = new();
    private int _maxJobNumber;

    public NewJobDialog(PoRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        OeBox.Text    = _repository.GetNextOeNumber().ToString();
        _maxJobNumber = _repository.GetMaxJobNumber();
        JobNoBox.Text = (_maxJobNumber + 1).ToString();

        _customers = _repository.GetAllCustomers();
        CustomerBox.ItemsSource   = _customers.Select(c => c.Name).ToList();
        CustomerBox.SelectedIndex = -1;
    }

    private void UsePrevJobBtn_Click(object sender, RoutedEventArgs e)
        => JobNoBox.Text = _maxJobNumber.ToString();

    private void CustomerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateContactAvailability();

    private void CustomerBox_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateContactAvailability();

    /// <summary>
    /// Enables/populates ContactBox based on the currently typed Customer name. Runs on both
    /// SelectionChanged (picking an existing customer from the dropdown) and TextChanged (typing
    /// a brand-new customer name, which never fires SelectionChanged) so Contact never gets stuck
    /// disabled while entering a new customer.
    /// </summary>
    private void UpdateContactAvailability()
    {
        string typed = CustomerBox.Text.Trim();
        if (string.IsNullOrEmpty(typed))
        {
            ContactBox.IsEnabled   = false;
            ContactBox.ItemsSource = null;
            ContactBox.Text        = string.Empty;
            return;
        }

        ContactBox.IsEnabled = true;
        var customer = _customers.FirstOrDefault(c => c.Name == typed);
        if (customer == null)
        {
            // New customer — no existing contacts to suggest, let the user type a new one freely.
            ContactBox.ItemsSource = null;
            return;
        }

        var contacts = _repository.GetContactsByCustomer(customer.Id);
        ContactBox.ItemsSource = contacts.Select(c => c.Name).ToList();
        if (contacts.Count == 1)
            ContactBox.Text = contacts[0].Name;
    }

    private void PartsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string drawingNumber = PartsBox.Text.Trim();
        if (string.IsNullOrEmpty(drawingNumber)) return;

        DrawingInfo? info = _drawingRepository.GetDrawingInfo(drawingNumber);
        if (info != null)
            DescBox.Text = info.Description;
    }

    private void PriceBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        string proposed = PriceBox.Text.Remove(PriceBox.SelectionStart, PriceBox.SelectionLength)
                                        .Insert(PriceBox.SelectionStart, e.Text);
        e.Handled = !PriceRegex.IsMatch(proposed);
    }

    private NewJobInput BuildInput()
    {
        string jobNo  = JobNoBox.Text.Trim();
        string poNum  = PoBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(poNum))
            poNum = $"NPO-{jobNo}";

        if (!int.TryParse(QtyBox.Text, out int qty)) qty = 1;
        if (!int.TryParse(LnBox.Text,  out int ln))  ln  = 1;

        return new NewJobInput
        {
            OeNumber      = OeBox.Text.Trim(),
            JobNumber     = jobNo,
            CustomerName  = CustomerBox.Text.Trim(),
            ContactName   = ContactBox.Text.Trim(),
            Quantity      = qty,
            DrawingNumber = PartsBox.Text.Trim(),
            Revision      = RevBox.Text.Trim(),
            LineNumber    = ln,
            Description   = DescBox.Text.Trim(),
            UnitPrice     = PriceBox.Text.Trim(),
            PoNumber      = poNum,
            PoRevision    = RevisionBox.Text.Trim(),
            DeliveryDate  = FormatDeliveryDate(DelPicker.Text)
        };
    }

    private static string FormatDeliveryDate(string rawText)
    {
        string trimmed = rawText.Trim();
        return DateTime.TryParse(trimmed, out var date) ? date.ToString("yyyy-MM-dd") : trimmed;
    }

    private bool ValidateInput(NewJobInput input)
    {
        if (string.IsNullOrWhiteSpace(input.JobNumber))
        {
            MessageBox.Show("Job Number is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            JobNoBox.Focus();
            return false;
        }
        if (string.IsNullOrWhiteSpace(input.CustomerName))
        {
            MessageBox.Show("Customer is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            CustomerBox.Focus();
            return false;
        }
        return true;
    }

    /// <summary>
    /// If the typed Customer and/or Contact name doesn't match an existing record, asks the user
    /// (in a single combined prompt) whether to create it. Returns true if it's safe to proceed
    /// (nothing new, or the user confirmed); false if the user declined.
    /// </summary>
    private bool ConfirmNewCustomerContact(NewJobInput input)
    {
        var customer = _customers.FirstOrDefault(c => c.Name == input.CustomerName);
        bool customerIsNew = customer == null;
        bool contactIsNew = false;

        if (!customerIsNew && !string.IsNullOrWhiteSpace(input.ContactName))
        {
            var contacts = _repository.GetContactsByCustomer(customer!.Id);
            contactIsNew = !contacts.Any(c => c.Name == input.ContactName);
        }
        else if (customerIsNew && !string.IsNullOrWhiteSpace(input.ContactName))
        {
            contactIsNew = true;
        }

        if (!customerIsNew && !contactIsNew) return true;

        string message = "The following do not exist yet and will be created:\n\n";
        if (customerIsNew) message += $"Customer: {input.CustomerName}\n";
        if (contactIsNew)  message += $"Contact: {input.ContactName}\n";
        message += "\nCreate them and proceed?";

        return MessageBox.Show(message, "New Customer/Contact", MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var input = BuildInput();
        if (!ValidateInput(input)) return;
        if (!ConfirmNewCustomerContact(input)) return;

        string summary = FormatSummary(input);
        if (MessageBox.Show(summary, "Confirm Create", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var (id, error) = _repository.CreateOrderItemCascade(input);
        if (id < 0)
        {
            MessageBox.Show($"Failed to create record: {error}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"NewJobDialog: CreateOrderItemCascade failed: {error}");
            return;
        }

        Logger.Instance.Info($"NewJobDialog: created order_item id={id}");
        DialogResult = true;
    }

    private void BatchCreateButton_Click(object sender, RoutedEventArgs e)
    {
        var input = BuildInput();
        if (!ValidateInput(input)) return;
        if (!ConfirmNewCustomerContact(input)) return;

        string summary = FormatSummary(input);
        if (MessageBox.Show(summary, "Confirm Batch Create", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var (id, error) = _repository.CreateOrderItemCascade(input);
        if (id < 0)
        {
            MessageBox.Show($"Failed to create record: {error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Logger.Instance.Info($"NewJobDialog: batch-created order_item id={id}");

        // Advance Job No and Ln; preserve Customer, Contact, Del. Req'd
        if (int.TryParse(JobNoBox.Text, out int jn)) JobNoBox.Text = (jn + 1).ToString();
        if (int.TryParse(LnBox.Text,   out int ln)) LnBox.Text   = (ln + 1).ToString();

        PartsBox.Text     = string.Empty;
        RevBox.Text       = string.Empty;
        DescBox.Text      = string.Empty;
        PriceBox.Text     = string.Empty;
        PoBox.Text        = string.Empty;
        RevisionBox.Text  = string.Empty;
        QtyBox.Text       = "1";
    }

    private void ReturnButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private static string FormatSummary(NewJobInput i) =>
        $"Job: {i.JobNumber}  |  OE: {i.OeNumber}\n" +
        $"Customer: {i.CustomerName}  |  Contact: {i.ContactName}\n" +
        $"P.O.: {i.PoNumber}  |  Rev: {i.PoRevision}  |  Ln: {i.LineNumber}  |  Qty: {i.Quantity}\n" +
        $"Drawing: {i.DrawingNumber}  Rev: {i.Revision}\n" +
        $"Description: {i.Description}\n" +
        $"Del. Req'd: {i.DeliveryDate}\n\nProceed?";
}
