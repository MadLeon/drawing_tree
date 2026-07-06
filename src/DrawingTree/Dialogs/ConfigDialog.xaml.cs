using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Data;
using DrawingTree.Services;

using Button              = System.Windows.Controls.Button;
using MessageBox          = System.Windows.MessageBox;
using MessageBoxButton    = System.Windows.MessageBoxButton;
using MessageBoxImage     = System.Windows.MessageBoxImage;
using MessageBoxResult    = System.Windows.MessageBoxResult;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace DrawingTree.Dialogs;

/// <summary>
/// ConfigDialog.xaml.cs
/// Settings dialog with Customer / Contact / Order Entry Log tabs.
/// </summary>
public partial class ConfigDialog : Window
{
    private readonly PoRepository _repository;
    private bool _suppressOeChange;

    public ConfigDialog(PoRepository repository)
    {
        _repository = repository;
        InitializeComponent();
        NavList.SelectedIndex = 0;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadCustomerTab();
        LoadContactTab();
        LoadOeTab();
    }

    // ── Nav ───────────────────────────────────────────────────────────────

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CustomerPanel.Visibility = NavList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContactPanel.Visibility  = NavList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        OePanel.Visibility       = NavList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Customer tab ──────────────────────────────────────────────────────

    private void LoadCustomerTab()
    {
        var customers = _repository.GetAllCustomers();
        var cards = customers.Select(c => new CustomerCardVm
        {
            Id            = c.Id,
            Name          = c.Name,
            DrawingFolder = _repository.GetFolderMapping(c.Id) ?? string.Empty,
            MpFolder      = MpConfig.GetFolderName(c.Name) ?? string.Empty,
            BubbleFolder  = BubbleConfig.GetBubbleFolder(c.Name) ?? string.Empty
        }).ToList();
        CustomerList.ItemsSource = cards;
    }

    private void NewCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewCustomerDialog(_repository) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            LoadCustomerTab();
            LoadContactTab();
        }
    }

    private void RemoveCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CustomerCardVm vm) return;

        if (MessageBox.Show($"Remove customer '{vm.Name}'? This cannot be undone.", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (!_repository.DeleteCustomer(vm.Id))
        {
            MessageBox.Show("Failed to remove customer. It may still have purchase orders on file.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadCustomerTab();
        LoadContactTab();
    }

    private void BrowseCustomerDrawingFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CustomerCardVm vm) return;
        string? path = BrowseFolder();
        if (path == null) return;
        _repository.UpsertFolderMapping(vm.Id, path);
        LoadCustomerTab();
    }

    private void BrowseCustomerMpFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CustomerCardVm vm) return;
        string? path = BrowseFolder();
        if (path == null) return;
        MpConfig.SetFolderName(vm.Name, path);
        LoadCustomerTab();
    }

    private void BrowseCustomerBubbleFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CustomerCardVm vm) return;
        string? path = BrowseFolder();
        if (path == null) return;
        BubbleConfig.SetBubbleFolder(vm.Name, path);
        LoadCustomerTab();
    }

    private static string? BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { ShowNewFolderButton = false };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    // ── Contact tab ───────────────────────────────────────────────────────

    private void LoadContactTab()
    {
        var customers = _repository.GetAllCustomers();
        var cards = customers.Select(c => new ContactCardVm
        {
            CustomerId   = c.Id,
            CustomerName = c.Name,
            Contacts     = _repository.GetContactDetailsByCustomer(c.Id)
        }).ToList();
        ContactCustomerList.ItemsSource = cards;
    }

    private void NewContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ContactCardVm vm) return;
        var dlg = new ContactEditDialog(_repository, vm.CustomerId) { Owner = this };
        if (dlg.ShowDialog() == true)
            LoadContactTab();
    }

    private void EditContact_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ContactDetailRow row) return;

        int customerId = FindCustomerIdForContact(row);
        if (customerId < 0) return;

        var dlg = new ContactEditDialog(_repository, customerId, row.Id, row.Name, row.Email) { Owner = this };
        if (dlg.ShowDialog() == true)
            LoadContactTab();
    }

    private void RemoveContact_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ContactDetailRow row) return;

        if (MessageBox.Show($"Remove contact '{row.Name}'?", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (!_repository.DeleteContact(row.Id))
        {
            MessageBox.Show("Failed to remove contact. It may still be referenced by a purchase order.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadContactTab();
    }

    private int FindCustomerIdForContact(ContactDetailRow row)
    {
        var cards = ContactCustomerList.ItemsSource as List<ContactCardVm>;
        return cards?.FirstOrDefault(c => c.Contacts.Any(x => x.Id == row.Id))?.CustomerId ?? -1;
    }

    // ── Order Entry Log tab ───────────────────────────────────────────────

    private void LoadOeTab()
    {
        _suppressOeChange = true;
        bool isOeView = OeViewConfig.GetLandingPage() == "oe view";
        OeViewRadio.IsChecked     = isOeView;
        SimpleViewRadio.IsChecked = !isOeView;
        _suppressOeChange = false;
    }

    private void LandingPageRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressOeChange) return;
        OeViewConfig.SetLandingPage(OeViewRadio.IsChecked == true ? "oe view" : "simple view");
    }
}

/// <summary>Display model for a customer card in the Config dialog's Customer tab.</summary>
public class CustomerCardVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DrawingFolder { get; set; } = string.Empty;
    public string MpFolder { get; set; } = string.Empty;
    public string BubbleFolder { get; set; } = string.Empty;
}

/// <summary>Display model for a customer's contact card in the Config dialog's Contact tab.</summary>
public class ContactCardVm
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public List<ContactDetailRow> Contacts { get; set; } = new();
}
