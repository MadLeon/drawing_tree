/// <summary>
/// AllPosControl.xaml.cs
/// Lists every order_item line across all purchase orders.
/// Supports OE view (DataGrid) and simple view (grouped, expandable rows).
/// </summary>

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DrawingTree.Data;
using DrawingTree.Dialogs;
using DrawingTree.Logging;

using UserControl      = System.Windows.Controls.UserControl;
using Button           = System.Windows.Controls.Button;
using MenuItem         = System.Windows.Controls.MenuItem;
using DataGrid         = System.Windows.Controls.DataGrid;
using DataGridRow      = System.Windows.Controls.DataGridRow;
using ContextMenu      = System.Windows.Controls.ContextMenu;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace DrawingTree.Controls;

// ── ViewModels ────────────────────────────────────────────────────────────────

public class PoSimpleGroup
{
    public int     PoId         { get; set; }
    public string  PoNumber     { get; set; } = string.Empty;
    public string? OeNumber     { get; set; }
    public string? CustomerName { get; set; }
    public string? ContactName  { get; set; }
    public ObservableCollection<ExpandableOrderItem> Items { get; } = new();
}

public class ExpandableOrderItem : INotifyPropertyChanged
{
    private bool _isExpanded;

    public PoListRow Row { get; set; } = null!;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandIcon));
            OnPropertyChanged(nameof(ChildrenVisibility));
        }
    }

    public bool HasPartId => Row.PartId.HasValue;

    public string ExpandIcon => IsExpanded ? "▾" : "▸";

    public Visibility ExpandButtonVisibility => HasPartId ? Visibility.Visible : Visibility.Hidden;

    public Visibility ChildrenVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<ChildDrawingItem> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ChildDrawingItem
{
    public string  DrawingNumber { get; set; } = string.Empty;
    public string? Revision      { get; set; }
    public string? Description   { get; set; }
}

// ── Control ───────────────────────────────────────────────────────────────────

public partial class AllPosControl : UserControl
{
    private readonly PoRepository _repository = new();
    private List<PoListRow> _allRows = new();
    private bool _isSimpleView;

    /// <summary>When true, shows is_active=0 POs and hides write buttons (History mode).</summary>
    public bool ShowHistoryOnly { get; set; }

    // ── Outbound events ──────────────────────────────────────────────────────

    /// <summary>Fired when the user clicks View on a PO line. Argument is purchase_order.id.</summary>
    public event EventHandler<int>? NavigateToPoRequested;
    /// <summary>Fired when the user chooses "Input Data" on a PO. Argument is the PO number.</summary>
    public event EventHandler<string>? ImportDrawingRequested;
    /// <summary>Fired when the user clicks the "Tree" button for a specific part.</summary>
    public event EventHandler<int>? NavigateToTreeRequested;
    /// <summary>Fired when the user clicks the History button.</summary>
    public event EventHandler? HistoryRequested;

    public AllPosControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ShowHistoryOnly)
        {
            TitleLabel.Text      = "PO History";
            NewJobButton.Visibility  = Visibility.Collapsed;
            HistoryButton.Visibility = Visibility.Collapsed;
        }
        LoadData();
    }

    public void Reload() => LoadData();

    private void LoadData()
    {
        _allRows = _repository.GetAllPoLines(activeOnly: !ShowHistoryOnly);

        if (_isSimpleView)
            ApplySimpleView();
        else
            ApplyOeView();

        Logger.Instance.Info($"AllPosControl: loaded {_allRows.Count} PO line(s) (historyMode={ShowHistoryOnly})");
    }

    // ── OE View ──────────────────────────────────────────────────────────────

    private void ApplyOeView()
    {
        var view = new ListCollectionView(_allRows);
        view.SortDescriptions.Add(new SortDescription(nameof(PoListRow.OeNumber),  ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(PoListRow.LineNumber), ListSortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PoListRow.PoNumber)));
        ResultsGrid.ItemsSource = view;

        ResultsGrid.Visibility     = Visibility.Visible;
        SimpleViewScroll.Visibility = Visibility.Collapsed;
        ToggleViewButton.Content   = "简洁视图";
    }

    // ── Simple View ───────────────────────────────────────────────────────────

    private void ApplySimpleView()
    {
        var groups = BuildSimpleGroups(_allRows);
        SimpleViewList.ItemsSource = groups;

        ResultsGrid.Visibility     = Visibility.Collapsed;
        SimpleViewScroll.Visibility = Visibility.Visible;
        ToggleViewButton.Content   = "OE 视图";
    }

    private static List<PoSimpleGroup> BuildSimpleGroups(List<PoListRow> rows)
    {
        var groups = new List<PoSimpleGroup>();
        foreach (var g in rows.GroupBy(r => r.PoId).OrderBy(g => g.First().OeNumber ?? g.First().PoNumber))
        {
            var first = g.First();
            var group = new PoSimpleGroup
            {
                PoId         = first.PoId,
                PoNumber     = first.PoNumber,
                OeNumber     = first.OeNumber,
                CustomerName = first.CustomerName,
                ContactName  = first.ContactName
            };
            foreach (var row in g.OrderBy(r => r.JobNumber).ThenBy(r => r.LineNumber))
                group.Items.Add(new ExpandableOrderItem { Row = row });
            groups.Add(group);
        }
        return groups;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void ToggleViewButton_Click(object sender, RoutedEventArgs e)
    {
        _isSimpleView = !_isSimpleView;
        if (_isSimpleView) ApplySimpleView();
        else               ApplyOeView();
    }

    private void NewJobButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewJobDialog(_repository) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Reload();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int poId)
        {
            Logger.Instance.Info($"AllPosControl: navigate to poId={poId}");
            NavigateToPoRequested?.Invoke(this, poId);
        }
    }

    // ── Simple view — PO header menu ─────────────────────────────────────────

    private void PoHeaderMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void MarkAsShipped_Click(object sender, RoutedEventArgs e)
    {
        var group = GetPoGroupFromMenuSender(sender);
        if (group == null) return;

        var result = MessageBox.Show(
            $"Mark PO '{group.PoNumber}' as shipped and remove it from the active list?",
            "Confirm Mark As Shipped",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        if (_repository.MarkAsShipped(group.PoId))
        {
            Logger.Instance.Info($"AllPosControl: poId={group.PoId} marked as shipped");
            Reload();
        }
        else
        {
            MessageBox.Show("Failed to update the PO status.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InputData_Click(object sender, RoutedEventArgs e)
    {
        var group = GetPoGroupFromMenuSender(sender);
        if (group == null) return;
        Logger.Instance.Info($"AllPosControl: Import Drawing requested for PO={group.PoNumber}");
        ImportDrawingRequested?.Invoke(this, group.PoNumber);
    }

    private static PoSimpleGroup? GetPoGroupFromMenuSender(object sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is ContextMenu cm && cm.PlacementTarget is Button btn && btn.Tag is PoSimpleGroup g)
            return g;
        return null;
    }

    private void SimplePoTreeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PoSimpleGroup group) return;
        var firstWithPart = group.Items.FirstOrDefault(i => i.Row.PartId.HasValue);
        if (firstWithPart == null)
        {
            MessageBox.Show("No drawing with a linked part found for this PO.", "No Part",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        NavigateToTreeRequested?.Invoke(this, firstWithPart.Row.PartId!.Value);
    }

    private void SimplePoDetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int poId)
            NavigateToPoRequested?.Invoke(this, poId);
    }

    // ── Simple view — order item row menu ────────────────────────────────────

    private void ItemRowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void ExpandItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ExpandableOrderItem item) return;

        if (item.IsExpanded)
        {
            item.IsExpanded = false;
            return;
        }

        if (!item.Row.PartId.HasValue) return;

        if (item.Children.Count == 0)
        {
            var children = _repository.GetChildDrawings(item.Row.PartId.Value);
            foreach (var c in children)
                item.Children.Add(new ChildDrawingItem
                {
                    DrawingNumber = c.DrawingNumber,
                    Revision      = c.Revision,
                    Description   = c.Description
                });
        }

        item.IsExpanded = item.Children.Count > 0;
        if (!item.IsExpanded)
            Logger.Instance.Info($"AllPosControl: no children found for partId={item.Row.PartId}");
    }

    // ── OE DataGrid right-click ───────────────────────────────────────────────

    private void ResultsGrid_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var hit = grid.InputHitTest(e.GetPosition(grid)) as FrameworkElement;
        var row = FindParent<DataGridRow>(hit);
        if (row?.DataContext is not PoListRow poRow) return;

        var menu = new System.Windows.Controls.ContextMenu();
        var editItem = new System.Windows.Controls.MenuItem { Header = "Edit Item", Tag = poRow };
        editItem.Click += EditItem_ContextMenu_Click;
        menu.Items.Add(editItem);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T t) return t;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    // ── Edit Item (shared between OE and simple views) ────────────────────────

    private void OeRowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void EditItem_ContextMenu_Click(object sender, RoutedEventArgs e)
    {
        PoListRow? row = GetPoListRowFromMenuSender(sender);
        if (row == null) return;

        var dlg = new EditItemDialog(_repository, row) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Reload();
    }

    private static PoListRow? GetPoListRowFromMenuSender(object sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is ContextMenu cm && cm.PlacementTarget is Button btn)
        {
            // Three-dot button in simple view: Tag = ExpandableOrderItem or PoListRow
            if (btn.Tag is ExpandableOrderItem eoi) return eoi.Row;
            if (btn.Tag is PoListRow r) return r;
        }
        // OE DataGrid right-click: MenuItem built in code with Tag = PoListRow
        if (mi.Tag is PoListRow row) return row;
        return null;
    }

}
