using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Data;
using DrawingTree.Logging;

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
    private readonly PoRepository _repository;
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
    {
        string? selected = CustomerBox.SelectedItem as string;
        var customer = _customers.FirstOrDefault(c => c.Name == selected);
        if (customer == null)
        {
            ContactBox.IsEnabled   = false;
            ContactBox.ItemsSource = null;
            ContactBox.Text        = string.Empty;
            return;
        }
        ContactBox.IsEnabled = true;
        var contacts = _repository.GetContactsByCustomer(customer.Id);
        ContactBox.ItemsSource = contacts.Select(c => c.Name).ToList();
        if (contacts.Count == 1)
            ContactBox.Text = contacts[0].Name;
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
            DeliveryDate  = DelBox.Text.Trim()
        };
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

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var input = BuildInput();
        if (!ValidateInput(input)) return;

        string summary = FormatSummary(input);
        if (MessageBox.Show(summary, "Confirm Create", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int id = _repository.CreateOrderItemCascade(input);
        if (id < 0)
        {
            MessageBox.Show("Failed to create record. Check the log for details.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"NewJobDialog: CreateOrderItemCascade returned -1");
            return;
        }

        Logger.Instance.Info($"NewJobDialog: created order_item id={id}");
        DialogResult = true;
    }

    private void BatchCreateButton_Click(object sender, RoutedEventArgs e)
    {
        var input = BuildInput();
        if (!ValidateInput(input)) return;

        string summary = FormatSummary(input);
        if (MessageBox.Show(summary, "Confirm Batch Create", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int id = _repository.CreateOrderItemCascade(input);
        if (id < 0)
        {
            MessageBox.Show("Failed to create record.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Logger.Instance.Info($"NewJobDialog: batch-created order_item id={id}");

        // Advance Job No and Ln; preserve Customer, Contact, Del. Req'd
        if (int.TryParse(JobNoBox.Text, out int jn)) JobNoBox.Text = (jn + 1).ToString();
        if (int.TryParse(LnBox.Text,   out int ln)) LnBox.Text   = (ln + 1).ToString();

        PartsBox.Text = string.Empty;
        RevBox.Text   = string.Empty;
        DescBox.Text  = string.Empty;
        PriceBox.Text = string.Empty;
        PoBox.Text    = string.Empty;
        QtyBox.Text   = "1";
    }

    private void ReturnButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private static string FormatSummary(NewJobInput i) =>
        $"Job: {i.JobNumber}  |  OE: {i.OeNumber}\n" +
        $"Customer: {i.CustomerName}  |  Contact: {i.ContactName}\n" +
        $"P.O.: {i.PoNumber}  |  Ln: {i.LineNumber}  |  Qty: {i.Quantity}\n" +
        $"Drawing: {i.DrawingNumber}  Rev: {i.Revision}\n" +
        $"Description: {i.Description}\n" +
        $"Del. Req'd: {i.DeliveryDate}\n\nProceed?";
}
