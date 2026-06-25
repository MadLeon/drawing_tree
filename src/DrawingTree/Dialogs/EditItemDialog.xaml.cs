using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Data;
using DrawingTree.Logging;

using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DrawingTree.Dialogs;

/// <summary>
/// EditItemDialog.xaml.cs
/// Dialog for editing an existing order_item and its associated part fields.
/// </summary>
public partial class EditItemDialog : Window
{
    private readonly PoRepository _repository;
    private readonly PoListRow _row;
    private List<CustomerRow> _customers = new();

    public EditItemDialog(PoRepository repository, PoListRow row)
    {
        _repository = repository;
        _row        = row;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleBlock.Text = $"{_row.PoNumber} / {_row.JobNumber}";

        _customers = _repository.GetAllCustomers();
        CustomerBox.ItemsSource = _customers.Select(c => c.Name).ToList();

        // Pre-fill fields
        CustomerBox.Text  = _row.CustomerName ?? string.Empty;
        LoadContacts(_row.CustomerName);
        ContactBox.Text   = _row.ContactName  ?? string.Empty;
        DrawingBox.Text   = _row.DrawingNumber ?? string.Empty;
        RevBox.Text       = _row.Revision      ?? string.Empty;
        DescBox.Text      = _row.Description   ?? string.Empty;
        QtyBox.Text       = _row.Quantity.ToString();
        LnBox.Text        = _row.LineNumber.ToString();
        DelBox.Text       = _row.DueDate       ?? string.Empty;
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
        var contacts = _repository.GetContactsByCustomer(customer.Id);
        ContactBox.ItemsSource = contacts.Select(c => c.Name).ToList();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(QtyBox.Text, out int qty)) qty = _row.Quantity;
        if (!int.TryParse(LnBox.Text,  out int ln))  ln  = _row.LineNumber;

        var input = new EditItemInput
        {
            OrderItemId   = _row.OrderItemId,
            DrawingNumber = DrawingBox.Text.Trim(),
            Revision      = RevBox.Text.Trim(),
            Description   = DescBox.Text.Trim(),
            Quantity      = qty,
            LineNumber    = ln,
            DeliveryDate  = DelBox.Text.Trim(),
            CustomerName  = CustomerBox.Text.Trim(),
            ContactName   = ContactBox.Text.Trim(),
            PoId          = _row.PoId
        };

        string summary =
            $"Drawing: {input.DrawingNumber}  Rev: {input.Revision}\n" +
            $"Customer: {input.CustomerName}  Contact: {input.ContactName}\n" +
            $"Qty: {input.Quantity}  Ln: {input.LineNumber}  Del: {input.DeliveryDate}\n\nSave changes?";

        if (MessageBox.Show(summary, "Confirm Edit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!_repository.UpdateOrderItemCascade(input))
        {
            MessageBox.Show("Failed to save changes. Check the log for details.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Instance.Error($"EditItemDialog: UpdateOrderItemCascade failed for orderItemId={_row.OrderItemId}");
            return;
        }

        Logger.Instance.Info($"EditItemDialog: updated order_item id={_row.OrderItemId}");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
