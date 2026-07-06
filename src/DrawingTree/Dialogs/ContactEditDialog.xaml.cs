using System.Windows;
using DrawingTree.Data;
using DrawingTree.Logging;

using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

namespace DrawingTree.Dialogs;

/// <summary>
/// ContactEditDialog.xaml.cs
/// Dialog for creating or editing a customer_contact row.
/// </summary>
public partial class ContactEditDialog : Window
{
    private readonly PoRepository _repository;
    private readonly int _customerId;
    private readonly int _contactId;
    private readonly bool _isEdit;

    /// <summary>Creates a dialog for adding a new contact under the given customer.</summary>
    public ContactEditDialog(PoRepository repository, int customerId)
    {
        _repository = repository;
        _customerId = customerId;
        _isEdit     = false;
        InitializeComponent();
        TitleBlock.Text = "New Contact";
    }

    /// <summary>Creates a dialog for editing an existing contact, pre-filled with its current values.</summary>
    public ContactEditDialog(PoRepository repository, int customerId, int contactId, string name, string? email)
    {
        _repository = repository;
        _customerId = customerId;
        _contactId  = contactId;
        _isEdit     = true;
        InitializeComponent();
        TitleBlock.Text = "Edit Contact";
        NameBox.Text  = name;
        EmailBox.Text = email ?? string.Empty;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string name  = NameBox.Text.Trim();
        string email = EmailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        bool success = _isEdit
            ? _repository.UpdateContact(_contactId, name, email)
            : _repository.AddContact(_customerId, name, email) >= 0;

        if (!success)
        {
            MessageBox.Show("Failed to save contact. Check the log for details.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Logger.Instance.Info($"ContactEditDialog: {(_isEdit ? "updated" : "created")} contact '{name}' for customerId={_customerId}");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
