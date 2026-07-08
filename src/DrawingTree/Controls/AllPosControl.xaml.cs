/// <summary>
/// AllPosControl.xaml.cs
/// Lists every order_item line across all purchase orders.
/// Supports OE view (DataGrid) and simple view (grouped, expandable rows).
/// </summary>

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Brush  = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using DrawingTree.Data;
using DrawingTree.Dialogs;
using DrawingTree.Logging;
using DrawingTree.Services;

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
using TextBlock        = System.Windows.Controls.TextBlock;

namespace DrawingTree.Controls;

// ── ViewModels ────────────────────────────────────────────────────────────────

public class PoSimpleGroup
{
    public int     PoId         { get; set; }
    public string  PoNumber     { get; set; } = string.Empty;
    public string? PoRevision   { get; set; }
    public string? OeNumber     { get; set; }
    public string? CustomerName { get; set; }
    public string? ContactName  { get; set; }
    public ObservableCollection<ExpandableOrderItem> Items { get; } = new();

    public string PoNumberDisplay => PoDisplayFormat.Format(PoNumber, PoRevision);
}

/// <summary>Shared expand/collapse icon geometries for order-item rows (both views).</summary>
public static class ExpandIcons
{
    public static readonly Geometry AddBoxGeo = Geometry.Parse("M440-280h80v-160h160v-80H520v-160h-80v160H280v80h160v160ZM200-120q-33 0-56.5-23.5T120-200v-560q0-33 23.5-56.5T200-840h560q33 0 56.5 23.5T840-760v560q0 33-23.5 56.5T760-120H200Zm0-80h560v-560H200v560Zm0-560v560-560Z");
    public static readonly Geometry IndeterminateBoxGeo = Geometry.Parse("M280-440h400v-80H280v80ZM200-120q-33 0-56.5-23.5T120-200v-560q0-33 23.5-56.5T200-840h560q33 0 56.5 23.5T840-760v560q0 33-23.5 56.5T760-120H200Zm0-80h560v-560H200v560Zm0-560v560-560Z");
}

/// <summary>Shared brush for a PDF icon whose file was not found on disk.</summary>
public static class PdfMissingFill
{
    public static readonly Brush Value = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xBB, 0xBB, 0xBB));
}

public class ExpandableOrderItem : INotifyPropertyChanged
{
    private bool _isExpanded;

    public PoListRow Row { get; set; } = null!;
    public string SearchTerm { get; set; } = string.Empty;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandIconGeometry));
            OnPropertyChanged(nameof(ChildrenVisibility));
        }
    }

    public Geometry ExpandIconGeometry => _isExpanded ? ExpandIcons.IndeterminateBoxGeo : ExpandIcons.AddBoxGeo;

    public bool HasMp { get; set; }
    public bool HasDir { get; set; }
    public bool HasPdf { get; set; }

    public Visibility ExpandButtonVisibility => Row.HasChildren ? Visibility.Visible : Visibility.Hidden;
    public Visibility PartButtonVisibility   => Row.PartId.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MpIconVisibility       => HasMp ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DirIconVisibility      => HasDir ? Visibility.Visible : Visibility.Collapsed;
    public Brush PdfIconFill                 => HasPdf ? Brushes.DodgerBlue : PdfMissingFill.Value;

    public Visibility ChildrenVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public Brush DrawingLinkForeground => Row.PartId.HasValue ? Brushes.DodgerBlue : Brushes.Black;
    public TextDecorationCollection? DrawingLinkDecorations => Row.PartId.HasValue ? TextDecorations.Underline : null;
    public Cursor DrawingLinkCursor => Row.PartId.HasValue ? Cursors.Hand : Cursors.Arrow;

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
    public int?    PartId        { get; set; }
    public int     OrderItemId   { get; set; }
    public string  SearchTerm    { get; set; } = string.Empty;

    public bool HasMp { get; set; }
    public bool HasDir { get; set; }
    public bool HasPdf { get; set; }

    public Brush DrawingLinkForeground => PartId.HasValue ? Brushes.DodgerBlue : Brushes.Black;
    public TextDecorationCollection? DrawingLinkDecorations => PartId.HasValue ? TextDecorations.Underline : null;
    public Cursor DrawingLinkCursor => PartId.HasValue ? Cursors.Hand : Cursors.Arrow;
    public Visibility PdfButtonVisibility => PartId.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MpIconVisibility    => HasMp ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DirIconVisibility   => HasDir ? Visibility.Visible : Visibility.Collapsed;
    public Brush PdfIconFill              => HasPdf ? Brushes.DodgerBlue : PdfMissingFill.Value;
}

/// <summary>
/// A row in the OE view grid. Represents either a top-level order-item row (backed by
/// <see cref="Row"/>) or an expanded child drawing row (backed by <see cref="ChildData"/>),
/// flattened into the same <see cref="DataGrid"/> so columns stay aligned.
/// </summary>
public class OeRowItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _hasPdf;

    public PoListRow Row { get; }
    public string SearchTerm { get; }
    public bool IsChildRow { get; init; }
    public ChildDrawingRow? ChildData { get; init; }

    /// <summary>True only for the first row of each P.O. group; drives the P.O. column hyperlink.</summary>
    public bool IsFirstInPoGroup { get; set; }

    /// <summary>Child OeRowItem instances currently inserted after this parent row (empty when collapsed).</summary>
    public List<OeRowItem> ChildRows { get; } = new();

    // Redundant on child rows by design — see issue #35 (child rows repeat the parent's columns).
    public int     PoId           => Row.PoId;
    public string  PoNumber       => Row.PoNumber;
    public string  PoNumberDisplay => Row.PoNumberDisplay;
    public string? OeNumber     => Row.OeNumber;
    public string  JobNumber    => Row.JobNumber;
    public int     LineNumber   => Row.LineNumber;
    public string? CustomerName => Row.CustomerName;
    public string? ContactName  => Row.ContactName;
    public int     Quantity     => Row.Quantity;
    public string? ReleaseDate  => Row.ReleaseDate;
    public string? DueDate      => Row.DueDate;
    public int     OrderItemId  => Row.OrderItemId;

    public string? DrawingNumber => IsChildRow ? ChildData!.DrawingNumber : Row.DrawingNumber;
    public string? Revision      => IsChildRow ? ChildData!.Revision      : Row.Revision;
    public string? Description   => IsChildRow ? ChildData!.Description  : Row.Description;
    public string? DescriptionDisplay => IsChildRow ? ChildData!.Description : Row.DescriptionDisplay;
    public int?    PartId        => IsChildRow ? ChildData!.PartId       : Row.PartId;

    public bool HasChildren => !IsChildRow && Row.HasChildren;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpandIconGeometry));
        }
    }

    public bool HasPdf
    {
        get => _hasPdf;
        set
        {
            _hasPdf = value;
            OnPropertyChanged(nameof(HasPdf));
            OnPropertyChanged(nameof(PdfIconFill));
        }
    }

    public Geometry   ExpandIconGeometry     => _isExpanded ? ExpandIcons.IndeterminateBoxGeo : ExpandIcons.AddBoxGeo;
    public Visibility ExpandButtonVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public Visibility PdfButtonVisibility    => PartId.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public Brush      PdfIconFill            => HasPdf ? Brushes.DodgerBlue : PdfMissingFill.Value;

    public Brush DrawingLinkForeground => PartId.HasValue ? Brushes.DodgerBlue : Brushes.Black;
    public TextDecorationCollection? DrawingLinkDecorations => PartId.HasValue ? TextDecorations.Underline : null;
    public Cursor DrawingLinkCursor => PartId.HasValue ? Cursors.Hand : Cursors.Arrow;

    public Brush PoLinkForeground => IsFirstInPoGroup ? Brushes.DodgerBlue : Brushes.Black;
    public TextDecorationCollection? PoLinkDecorations => IsFirstInPoGroup ? TextDecorations.Underline : null;
    public Cursor PoLinkCursor => IsFirstInPoGroup ? Cursors.Hand : Cursors.Arrow;

    public OeRowItem(PoListRow row, string searchTerm)
    {
        Row = row;
        SearchTerm = searchTerm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Column visibility toggles for the OE view DataGrid. In-memory only, resets on control construction.</summary>
public class OeColumnConfig : INotifyPropertyChanged
{
    private bool _showOe = true, _showLine = true, _showCustomer = true, _showContact = true,
                 _showQty = true, _showRev = true, _showDescription = true, _showRlsDate = true, _showDueDate = true;

    public bool ShowOe          { get => _showOe;          set { _showOe = value;          OnChanged(); } }
    public bool ShowLine        { get => _showLine;        set { _showLine = value;        OnChanged(); } }
    public bool ShowCustomer    { get => _showCustomer;    set { _showCustomer = value;    OnChanged(); } }
    public bool ShowContact     { get => _showContact;     set { _showContact = value;     OnChanged(); } }
    public bool ShowQty         { get => _showQty;         set { _showQty = value;         OnChanged(); } }
    public bool ShowRev         { get => _showRev;         set { _showRev = value;         OnChanged(); } }
    public bool ShowDescription { get => _showDescription; set { _showDescription = value; OnChanged(); } }
    public bool ShowRlsDate     { get => _showRlsDate;     set { _showRlsDate = value;     OnChanged(); } }
    public bool ShowDueDate     { get => _showDueDate;     set { _showDueDate = value;     OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ── Control ───────────────────────────────────────────────────────────────────

public partial class AllPosControl : UserControl
{
    private readonly PoRepository _repository = new();
    private List<PoListRow> _allRows = new();
    private HashSet<int> _pdfPartIds = new();
    private HashSet<int> _mpPartIds = new();
    private HashSet<int> _dirPartIds = new();
    private Dictionary<int, List<ChildDrawingRow>> _childrenByPartId = new();
    private bool _isSimpleView;
    private bool _isDataLoaded;
    private readonly DispatcherTimer _debounceTimer;
    private readonly ObservableCollection<OeRowItem> _oeItems = new();
    private ListCollectionView? _oeView;

    // ── Filter panel state (Customer / Contact / Due Date range) ────────────
    private string? _filterCustomer;
    private string? _filterContact;
    private DateTime? _filterDueFrom;
    private DateTime? _filterDueTo;

    // ── SearchTerm dependency property ───────────────────────────────────────

    public static readonly DependencyProperty SearchTermProperty =
        DependencyProperty.Register(nameof(SearchTerm), typeof(string),
            typeof(AllPosControl), new PropertyMetadata(string.Empty));

    /// <summary>Current search keyword; bound from DataTemplate elements via RelativeSource.</summary>
    public string SearchTerm
    {
        get => (string)GetValue(SearchTermProperty);
        set => SetValue(SearchTermProperty, value);
    }

    /// <summary>When true, shows is_active=0 POs and hides write buttons (History mode).</summary>
    public bool ShowHistoryOnly { get; set; }

    // ── Outbound events ──────────────────────────────────────────────────────

    /// <summary>Fired when the user clicks View on a PO line. Argument is purchase_order.id.</summary>
    public event EventHandler<int>? NavigateToPoRequested;
    /// <summary>Fired when the user chooses "Input Data" on a PO. Argument is the PO number.</summary>
    public event EventHandler<string>? ImportDrawingRequested;
    /// <summary>Fired when the user clicks the "Tree" button for a PO. Argument is the PO number.</summary>
    public event EventHandler<string>? NavigateToTreeRequested;
    /// <summary>Fired when the user clicks the part icon on an order item row. Args: (PartId, OrderItemId).</summary>
    public event EventHandler<(int PartId, int OrderItemId)>? NavigateToPartRequested;
    /// <summary>Fired when the user clicks the History button.</summary>
    public event EventHandler? HistoryRequested;
    /// <summary>Fired when the user chooses "Edit Parts" from a PO menu.</summary>
    public event EventHandler? EditPartsRequested;

    public AllPosControl()
    {
        InitializeComponent();

        _isSimpleView = OeViewConfig.GetLandingPage() != "oe view";

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            var term = PoSearchBox.Text.Trim();
            SearchTerm = term;
            ApplySearch(term);
        };

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ShowHistoryOnly)
        {
            TitleLabel.Text          = "PO History";
            NewJobButton.Visibility  = Visibility.Collapsed;
            HistoryButton.Visibility = Visibility.Collapsed;
        }
        if (!_isDataLoaded)
            _ = LoadDataAsync();
    }

    public void Reload() => _ = LoadDataAsync();

    private async Task LoadDataAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        bool activeOnly = !ShowHistoryOnly;

        (_allRows, _pdfPartIds, _mpPartIds, _dirPartIds, _childrenByPartId) = await Task.Run(() =>
        {
            var rows = _repository.GetAllPoLines(activeOnly: activeOnly);

            // Prefetch children for every top-level part that has any, up front — shared by
            // both OE and Simple views so expanding/switching never queries the database again.
            var rootPartIds = rows.Where(r => r.HasChildren && r.PartId.HasValue)
                                   .Select(r => r.PartId!.Value).Distinct().ToList();
            var childrenByPartId = _repository.GetAllChildDrawings(rootPartIds);

            var allPartIds = rows.Where(r => r.PartId.HasValue).Select(r => r.PartId!.Value)
                .Concat(childrenByPartId.Values.SelectMany(list => list.Select(c => c.PartId)))
                .Distinct().ToList();

            var pdfSet = _repository.GetPartIdsWithPdf(allPartIds);
            var mpSet  = _repository.GetPartIdsWithMp(allPartIds);
            var dirSet = _repository.GetPartIdsWithDir(allPartIds);

            return (rows, pdfSet, mpSet, dirSet, childrenByPartId);
        });

        int poCount = _allRows.Select(r => r.PoId).Distinct().Count();
        PoCountLabel.Text = $"({poCount})";

        ApplySearch(SearchTerm);

        _isDataLoaded = true;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        Logger.Instance.Info($"AllPosControl: loaded {_allRows.Count} PO line(s), " +
            $"{_childrenByPartId.Count} part(s) with children (historyMode={ShowHistoryOnly})");
    }

    // ── Search / filter ───────────────────────────────────────────────────────

    private void ApplySearch(string term)
    {
        bool hasFilter = _filterCustomer != null || _filterContact != null
            || _filterDueFrom.HasValue || _filterDueTo.HasValue;

        List<PoListRow> filtered;
        if (string.IsNullOrEmpty(term) && !hasFilter)
        {
            filtered = _allRows;
        }
        else
        {
            var matchingPoIds = _allRows
                .Where(r => (string.IsNullOrEmpty(term) || RowMatchesTerm(r, term)) && RowMatchesFilters(r))
                .Select(r => r.PoId)
                .ToHashSet();
            filtered = _allRows.Where(r => matchingPoIds.Contains(r.PoId)).ToList();
        }

        if (_isSimpleView) ApplySimpleView(filtered);
        else               ApplyOeView(filtered);
    }

    private static bool RowMatchesTerm(PoListRow r, string term) =>
        Contains(r.PoNumber,     term) || Contains(r.OeNumber,    term) ||
        Contains(r.JobNumber,    term) || Contains(r.DrawingNumber, term) ||
        Contains(r.Description,  term) || Contains(r.CustomerName, term);

    private static bool Contains(string? text, string term) =>
        text != null && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private bool RowMatchesFilters(PoListRow r)
    {
        if (_filterCustomer != null && !string.Equals(r.CustomerName, _filterCustomer, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_filterContact != null && !string.Equals(r.ContactName, _filterContact, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_filterDueFrom.HasValue || _filterDueTo.HasValue)
        {
            if (!DateTime.TryParse(r.DueDate, out var due)) return false;
            if (_filterDueFrom.HasValue && due.Date < _filterDueFrom.Value.Date) return false;
            if (_filterDueTo.HasValue   && due.Date > _filterDueTo.Value.Date)   return false;
        }
        return true;
    }

    private void PoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PoPlaceholderText.Visibility = PoSearchBox.Text.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    // ── Filter panel (Customer / Contact / Due Date range) ─────────────────────

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (!FilterPopup.IsOpen)
        {
            var customers = _allRows
                .Where(r => !string.IsNullOrEmpty(r.CustomerName))
                .Select(r => r.CustomerName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
            customers.Insert(0, "");
            FilterCustomerBox.ItemsSource = customers;
            FilterCustomerBox.SelectedItem = _filterCustomer ?? "";

            UpdateContactOptions(_filterCustomer);
            FilterContactBox.SelectedItem = _filterContact ?? "";

            FilterDueFromPicker.SelectedDate = _filterDueFrom;
            FilterDueToPicker.SelectedDate   = _filterDueTo;
        }
        FilterPopup.IsOpen = !FilterPopup.IsOpen;
    }

    private void FilterCustomerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateContactOptions(FilterCustomerBox.SelectedItem as string);
        FilterContactBox.SelectedItem = "";
    }

    private void UpdateContactOptions(string? customer)
    {
        if (string.IsNullOrEmpty(customer))
        {
            FilterContactBox.ItemsSource = null;
            FilterContactBox.IsEnabled = false;
            return;
        }

        var contacts = _allRows
            .Where(r => string.Equals(r.CustomerName, customer, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(r.ContactName))
            .Select(r => r.ContactName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
        contacts.Insert(0, "");
        FilterContactBox.ItemsSource = contacts;
        FilterContactBox.IsEnabled = true;
    }

    private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _filterCustomer = string.IsNullOrEmpty(FilterCustomerBox.SelectedItem as string) ? null : (string)FilterCustomerBox.SelectedItem;
        _filterContact  = string.IsNullOrEmpty(FilterContactBox.SelectedItem as string)  ? null : (string)FilterContactBox.SelectedItem;
        _filterDueFrom  = FilterDueFromPicker.SelectedDate;
        _filterDueTo    = FilterDueToPicker.SelectedDate;

        FilterPopup.IsOpen = false;
        ApplySearch(SearchTerm);
    }

    private void CancelFilterButton_Click(object sender, RoutedEventArgs e)
    {
        FilterPopup.IsOpen = false;
    }

    // ── Columns panel (OE view DataGrid column visibility) ──────────────────────

    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        ColumnsPopup.IsOpen = !ColumnsPopup.IsOpen;
    }

    // ── OE View ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the flattened OE grid rows. Backed by an ObservableCollection (rather than a
    /// fresh List/ListCollectionView per call) so expand/collapse can Insert/Remove child rows
    /// in place — see OeExpandItem_Click. Expand state is not preserved across search/reload,
    /// matching the simple view's existing behavior (BuildSimpleGroups also rebuilds from scratch).
    /// </summary>
    private void ApplyOeView(List<PoListRow>? rows = null)
    {
        _oeItems.Clear();
        foreach (var g in (rows ?? _allRows)
                     .GroupBy(r => r.PoId)
                     .OrderBy(g => g.First().OeNumber ?? g.First().PoNumber))
        {
            bool isFirst = true;
            foreach (var row in g.OrderBy(r => r.JobNumber).ThenBy(r => r.LineNumber))
            {
                _oeItems.Add(new OeRowItem(row, SearchTerm)
                {
                    IsFirstInPoGroup = isFirst,
                    HasPdf = row.PartId.HasValue && _pdfPartIds.Contains(row.PartId.Value)
                });
                isFirst = false;
            }
        }

        if (_oeView == null)
        {
            _oeView = new ListCollectionView(_oeItems);
            _oeView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OeRowItem.PoNumber)));
            ResultsGrid.ItemsSource = _oeView;
        }

        ResultsGrid.Visibility     = Visibility.Visible;
        SimpleViewScroll.Visibility = Visibility.Collapsed;
        ToggleViewButton.Content   = "Simple View";
        FilterButton.Visibility   = Visibility.Visible;
        ColumnsButton.Visibility  = Visibility.Visible;
    }

    // ── Simple View ───────────────────────────────────────────────────────────

    private void ApplySimpleView(List<PoListRow>? rows = null)
    {
        var groups = BuildSimpleGroups(rows ?? _allRows, SearchTerm);

        foreach (var item in groups.SelectMany(g => g.Items))
        {
            item.HasMp  = item.Row.PartId.HasValue && _mpPartIds.Contains(item.Row.PartId.Value);
            item.HasDir = item.Row.PartId.HasValue && _dirPartIds.Contains(item.Row.PartId.Value);
            item.HasPdf = item.Row.PartId.HasValue && _pdfPartIds.Contains(item.Row.PartId.Value);

            if (item.Row.HasChildren && item.Row.PartId.HasValue &&
                _childrenByPartId.TryGetValue(item.Row.PartId.Value, out var children))
            {
                foreach (var c in children)
                    item.Children.Add(new ChildDrawingItem
                    {
                        DrawingNumber = c.DrawingNumber,
                        Revision      = c.Revision,
                        Description   = c.Description,
                        PartId        = c.PartId,
                        OrderItemId   = item.Row.OrderItemId,
                        SearchTerm    = SearchTerm,
                        HasMp  = _mpPartIds.Contains(c.PartId),
                        HasDir = _dirPartIds.Contains(c.PartId),
                        HasPdf = _pdfPartIds.Contains(c.PartId)
                    });
            }
        }

        SimpleViewList.ItemsSource = groups;

        ResultsGrid.Visibility      = Visibility.Collapsed;
        SimpleViewScroll.Visibility = Visibility.Visible;
        ToggleViewButton.Content    = "OE View";
        FilterButton.Visibility    = Visibility.Collapsed;
        ColumnsButton.Visibility   = Visibility.Collapsed;
    }

    private static List<PoSimpleGroup> BuildSimpleGroups(List<PoListRow> rows, string term = "")
    {
        var groups = new List<PoSimpleGroup>();
        foreach (var g in rows.GroupBy(r => r.PoId).OrderBy(g => g.First().OeNumber ?? g.First().PoNumber))
        {
            var first = g.First();
            var group = new PoSimpleGroup
            {
                PoId         = first.PoId,
                PoNumber     = first.PoNumber,
                PoRevision   = first.PoRevision,
                OeNumber     = first.OeNumber,
                CustomerName = first.CustomerName,
                ContactName  = first.ContactName
            };
            foreach (var row in g.OrderBy(r => r.JobNumber).ThenBy(r => r.LineNumber))
                group.Items.Add(new ExpandableOrderItem { Row = row, SearchTerm = term });
            groups.Add(group);
        }
        return groups;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void ToggleViewButton_Click(object sender, RoutedEventArgs e)
    {
        _isSimpleView = !_isSimpleView;
        FilterPopup.IsOpen = false;
        ColumnsPopup.IsOpen = false;
        ApplySearch(SearchTerm);
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

    private void EditParts_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("AllPosControl: Edit Parts requested");
        EditPartsRequested?.Invoke(this, EventArgs.Empty);
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
        NavigateToTreeRequested?.Invoke(this, group.PoNumber);
    }

    private void SimplePoDetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int poId)
            NavigateToPoRequested?.Invoke(this, poId);
    }

    // ── Simple view — order item row menu ────────────────────────────────────

    private void OpenPartButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ExpandableOrderItem item) return;
        if (!item.Row.PartId.HasValue) return;
        OpenPdfExternal(_repository.GetPdfPath(item.Row.PartId.Value));
    }

    private void OpenChildPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ChildDrawingItem child) return;
        if (!child.PartId.HasValue) return;
        OpenPdfExternal(_repository.GetPdfPath(child.PartId.Value));
    }

    private void DrawingNumberMain_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ExpandableOrderItem item && item.Row.PartId.HasValue)
        {
            NavigateToPartRequested?.Invoke(this, (item.Row.PartId.Value, item.Row.OrderItemId));
            e.Handled = true;
        }
    }

    private void DrawingNumberChild_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ChildDrawingItem child && child.PartId.HasValue)
        {
            NavigateToPartRequested?.Invoke(this, (child.PartId.Value, child.OrderItemId));
            e.Handled = true;
        }
    }

    private static void OpenPdfExternal(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // GetPartIdsWithPdf no longer probes the disk (DB-only, to avoid network-share I/O
            // during list load), so a missing file surfaces here instead of at load time.
            Logger.Instance.Warning($"OpenPdfExternal: file not found, path='{path}'");
            MessageBox.Show("The PDF file was not found on disk. It may have been moved or deleted.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open PDF: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
            var pdfSet = _repository.GetPartIdsWithPdf(children.Select(c => c.PartId).Distinct());
            foreach (var c in children)
                item.Children.Add(new ChildDrawingItem
                {
                    DrawingNumber = c.DrawingNumber,
                    Revision      = c.Revision,
                    Description   = c.Description,
                    PartId        = c.PartId,
                    OrderItemId   = item.Row.OrderItemId,
                    SearchTerm    = SearchTerm,
                    HasPdf        = pdfSet.Contains(c.PartId)
                });
        }

        item.IsExpanded = item.Children.Count > 0;
        if (!item.IsExpanded)
            Logger.Instance.Info($"AllPosControl: no children found for partId={item.Row.PartId}");
    }

    // ── OE view — expand / hyperlinks / PDF ──────────────────────────────────

    private void OeExpandItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OeRowItem item || !item.HasChildren) return;

        if (item.IsExpanded)
        {
            foreach (var child in item.ChildRows)
                _oeItems.Remove(child);
            item.ChildRows.Clear();
            item.IsExpanded = false;
            return;
        }

        if (!item.Row.PartId.HasValue) return;

        if (!_childrenByPartId.TryGetValue(item.Row.PartId.Value, out var children) || children.Count == 0)
        {
            Logger.Instance.Info($"AllPosControl: no children found for partId={item.Row.PartId}");
            return;
        }

        int insertAt = _oeItems.IndexOf(item) + 1;
        foreach (var c in children)
        {
            var childItem = new OeRowItem(item.Row, SearchTerm)
            {
                IsChildRow = true,
                ChildData  = c,
                HasPdf     = _pdfPartIds.Contains(c.PartId)
            };
            _oeItems.Insert(insertAt++, childItem);
            item.ChildRows.Add(childItem);
        }

        item.IsExpanded = true;
    }

    private void OePoNumber_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OeRowItem item && item.IsFirstInPoGroup)
        {
            NavigateToPoRequested?.Invoke(this, item.PoId);
            e.Handled = true;
        }
    }

    private void OeDrawingNumber_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is OeRowItem item && item.PartId.HasValue)
        {
            NavigateToPartRequested?.Invoke(this, (item.PartId.Value, item.OrderItemId));
            e.Handled = true;
        }
    }

    private void OpenOePdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OeRowItem item) return;
        if (!item.PartId.HasValue) return;
        OpenPdfExternal(_repository.GetPdfPath(item.PartId.Value));
    }

    // ── Copy text (right-click menu on highlight-mode TextBlocks) ────────────

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is TextBlock tb)
            System.Windows.Clipboard.SetText(tb.Text);
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
            PatchAfterEdit(row, dlg.SavedInput!, dlg.Result!);
    }

    /// <summary>
    /// Applies an EditItemDialog save to the in-memory _allRows cache and re-renders the current
    /// view, without re-querying the database. Patches by part_id (UpsertPart's ON CONFLICT can
    /// silently update a part shared by other order_items/POs — see UpsertPart) and by po_id
    /// (customer/contact live on purchase_order, shared by every line under that PO).
    /// </summary>
    private void PatchAfterEdit(PoListRow oldRow, EditItemInput input, EditItemResult result)
    {
        bool contactChanged = !string.IsNullOrWhiteSpace(input.ContactName);

        for (int i = 0; i < _allRows.Count; i++)
        {
            var r = _allRows[i];
            bool isEditedRow = r.OrderItemId == input.OrderItemId;
            bool sharesPart = result.PartUpdated && r.PartId.HasValue
                && (r.PartId.Value == oldRow.PartId || r.PartId.Value == result.PartId);
            bool sharesPo = contactChanged && r.PoId == input.PoId;

            if (!isEditedRow && !sharesPart && !sharesPo) continue;

            var updated = r;
            if (isEditedRow)
            {
                updated = updated with
                {
                    LineNumber    = input.LineNumber,
                    Quantity      = input.Quantity,
                    DueDate       = input.DeliveryDate,
                    DrawingNumber = result.PartUpdated ? input.DrawingNumber : updated.DrawingNumber,
                    Revision      = result.PartUpdated ? input.Revision      : updated.Revision,
                    PartId        = result.PartUpdated ? result.PartId      : updated.PartId
                };
            }
            if (sharesPart)
                updated = updated with { Description = result.Description };
            if (sharesPo)
                updated = updated with { CustomerName = input.CustomerName, ContactName = input.ContactName };

            _allRows[i] = updated;
        }

        ApplySearch(SearchTerm);
        Logger.Instance.Info($"AllPosControl: patched cache after edit of order_item id={input.OrderItemId}");
    }

    private static PoListRow? GetPoListRowFromMenuSender(object sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.Parent is ContextMenu cm && cm.PlacementTarget is Button btn)
        {
            // Three-dot button in simple view: Tag = ExpandableOrderItem or PoListRow or OeRowItem
            if (btn.Tag is ExpandableOrderItem eoi) return eoi.Row;
            if (btn.Tag is OeRowItem oe) return oe.Row;
            if (btn.Tag is PoListRow r) return r;
        }
        // OE DataGrid right-click: MenuItem built in code with Tag = PoListRow
        if (mi.Tag is PoListRow row) return row;
        return null;
    }

}
