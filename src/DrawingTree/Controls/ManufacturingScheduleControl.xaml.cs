/// <summary>
/// ManufacturingScheduleControl.xaml.cs
/// Gantt-style Manufacturing Schedule page. Left panel lists all order items from
/// active POs; right Canvas displays coloured step-tracker bars per row.
/// </summary>

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingTree.Data;
using DrawingTree.Dialogs;
using DrawingTree.Logging;

using Color            = System.Windows.Media.Color;
using Brushes          = System.Windows.Media.Brushes;
using Point            = System.Windows.Point;
using Rectangle        = System.Windows.Shapes.Rectangle;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

using UserControl = System.Windows.Controls.UserControl;

namespace DrawingTree.Controls;

// ── Column visibility config ───────────────────────────────────────────────

public class ColumnConfig : INotifyPropertyChanged
{
    private bool _showPo          = true;
    private bool _showCustomer    = true;
    private bool _showDescription = true;
    private bool _showQuantity    = true;
    private bool _showDueDate     = true;
    private bool _showMemo        = true;
    private bool _showStatus      = true;

    public bool ShowPo          { get => _showPo;          set { _showPo = value;          OnChanged(); } }
    public bool ShowCustomer    { get => _showCustomer;    set { _showCustomer = value;    OnChanged(); } }
    public bool ShowDescription { get => _showDescription; set { _showDescription = value; OnChanged(); } }
    public bool ShowQuantity    { get => _showQuantity;    set { _showQuantity = value;    OnChanged(); } }
    public bool ShowDueDate     { get => _showDueDate;     set { _showDueDate = value;     OnChanged(); } }
    public bool ShowMemo        { get => _showMemo;        set { _showMemo = value;        OnChanged(); } }
    public bool ShowStatus      { get => _showStatus;      set { _showStatus = value;      OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ── Schedule Display Row ───────────────────────────────────────────────────

/// <summary>
/// Flat display item for LeftRows: either a parent order-item row or a child BOM row.
/// Using a flat list ensures the GanttCanvas bands stay vertically aligned with LeftRows.
/// </summary>
public class ScheduleDisplayRow : INotifyPropertyChanged
{
    private static readonly Geometry AddBoxGeo;
    private static readonly Geometry IndeterminateBoxGeo;

    static ScheduleDisplayRow()
    {
        AddBoxGeo = Geometry.Parse("M440-280h80v-160h160v-80H520v-160h-80v160H280v80h160v160ZM200-120q-33 0-56.5-23.5T120-200v-560q0-33 23.5-56.5T200-840h560q33 0 56.5 23.5T840-760v560q0 33-23.5 56.5T760-120H200Zm0-80h560v-560H200v560Zm0-560v560-560Z");
        AddBoxGeo.Freeze();
        IndeterminateBoxGeo = Geometry.Parse("M280-440h400v-80H280v80Zm-80 320q-33 0-56.5-23.5T120-200v-560q0-33 23.5-56.5T200-840h560q33 0 56.5 23.5T840-760v560q0 33-23.5 56.5T760-120H200Zm0-80h560v-560H200v560Zm0-560v560-560Z");
        IndeterminateBoxGeo.Freeze();
    }

    public bool               IsChild     { get; init; }
    public ScheduleViewModel? Vm          { get; init; }

    private bool _hasChildren;
    public bool HasChildren
    {
        get => _hasChildren;
        set { _hasChildren = value; OnChanged(); OnChanged(nameof(ExpandButtonVisibility)); }
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnChanged(); OnChanged(nameof(ExpandIconGeometry)); }
    }

    public Geometry ExpandIconGeometry   => _isExpanded ? IndeterminateBoxGeo : AddBoxGeo;
    public Visibility ExpandButtonVisibility =>
        !IsChild && _hasChildren ? Visibility.Visible : Visibility.Hidden;

    // Child-only
    public int ChildPartId { get; init; }
    public int ParentOiId  { get; init; }

    // Display fields
    public string? DrawingNumber { get; init; }
    public string? Description   { get; init; }
    public string  QtyText       { get; init; } = "";
    public List<ScheduleStepTracker> Steps { get; init; } = [];

    private string? _memoText;
    public string? MemoText
    {
        get => _memoText;
        set { _memoText = value; OnChanged(); }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnChanged(); }
    }

    // Parent pass-through properties
    public string? PoNumber     => Vm?.Row.PoNumber;
    public string? JobNumber    => Vm?.Row.JobNumber;
    public string? CustomerName => Vm?.Row.CustomerName;
    public string? DueDate      => Vm?.Row.DueDate;
    public bool    IsOverdue    => Vm?.IsOverdue ?? false;
    public int     PartId       => IsChild ? ChildPartId : (Vm?.Row.PartId ?? 0);
    public int     OrderItemId  => IsChild ? 0 : (Vm?.Row.OrderItemId ?? 0);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ── DataTemplate Selector ─────────────────────────────────────────────────

/// <summary>Selects parent or child DataTemplate for schedule display rows.</summary>
public class ScheduleRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ParentTemplate { get; set; }
    public DataTemplate? ChildTemplate  { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is ScheduleDisplayRow r && r.IsChild ? ChildTemplate : ParentTemplate;
}

// ── Control ────────────────────────────────────────────────────────────────

public partial class ManufacturingScheduleControl : UserControl
{
    // ── Configuration ──────────────────────────────────────────────────────

    private const int RowHeight = 32;

    private const double DayViewPixelsPerDay   = 30.0;
    private const int    DayViewDaysBefore     = 30;
    private const int    DayViewDaysAfter      = 90;

    private const double WeekViewPixelsPerDay  = 20.0;
    private const int    WeekViewDaysBefore    = 56;
    private const int    WeekViewDaysAfter     = 112;

    private const double MonthViewPixelsPerDay = 4.0;
    private const int    MonthViewDaysBefore   = 90;
    private const int    MonthViewDaysAfter    = 275;

    private static readonly Dictionary<string, Color> ShopColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MILL"]  = Color.FromRgb(70,  130, 180),
        ["LATHE"] = Color.FromRgb(60,  179, 113),
        ["GRIND"] = Color.FromRgb(255, 140,   0),
        ["HEAT"]  = Color.FromRgb(220,  20,  60),
        ["COAT"]  = Color.FromRgb(147, 112, 219),
        ["INSP"]  = Color.FromRgb(32,  178, 170),
        ["WELD"]  = Color.FromRgb(255, 165,   0),
        ["ASSY"]  = Color.FromRgb(100, 149, 237),
    };

    private static readonly Color[] FallbackPalette =
    [
        Color.FromRgb(176, 196, 222),
        Color.FromRgb(144, 238, 144),
        Color.FromRgb(255, 218, 185),
        Color.FromRgb(216, 191, 216),
        Color.FromRgb(175, 238, 238),
    ];

    // ── Column widths (must match XAML DataTemplate Border widths) ─────────

    private const int ColWidthExpand      = 24;
    private const int ColWidthPo          = 125;
    private const int ColWidthJob         = 80;
    private const int ColWidthCustomer    = 100;
    private const int ColWidthDrawing     = 200;
    private const int ColWidthDescription = 150;
    private const int ColWidthQuantity    = 50;
    private const int ColWidthDueDate     = 90;
    private const int ColWidthMemo        = 80;
    private const int ColWidthStatus      = 120;

    // ── State ──────────────────────────────────────────────────────────────

    private readonly ScheduleRepository _repository = new();
    private List<ScheduleViewModel>     _allViewModels = [];
    private List<ScheduleViewModel>     _viewModels    = [];
    private Dictionary<int, List<ScheduleChildItemData>> _childDataMap = new();
    private Dictionary<(int OiId, int ChildPartId), List<ScheduleStepTracker>> _childStepMap = new();
    private readonly HashSet<int>       _expandedOiIds = [];
    private List<ScheduleDisplayRow>    _displayRows   = [];
    private ColumnConfig                _colCfg        = null!;

    private enum GanttViewMode { Day, Week, Month }
    private enum SortColumn    { None, Job, Customer, DueDate }

    private GanttViewMode _viewMode     = GanttViewMode.Day;
    private SortColumn    _sortColumn   = SortColumn.Job;
    private bool          _sortAscending = true;

    private DateTime _viewportStart;
    private double   _pixelsPerDay;
    private int      _totalDays;

    private readonly List<(Rect Bounds, int RowIndex, ScheduleStepTracker Step)> _barHitList = [];

    private double _ganttOffset = 0;
    private bool   _loaded      = false;

    public event EventHandler? BackRequested;
    public event EventHandler<(int PartId, int OrderItemId)>? OpenPartRequested;

    // ── Construction ──────────────────────────────────────────────────────

    public ManufacturingScheduleControl()
    {
        InitializeComponent();
        _colCfg = (ColumnConfig)Resources["ColCfg"];
        _colCfg.PropertyChanged += (_, _) => UpdateColumnVisibility();

        Loaded += OnLoaded;
        TimeHeaderCanvas.SizeChanged += (_, _) => DrawTimeHeader();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        ApplyViewMode();
        GanttClip.SizeChanged += (_, _) => UpdateGanttScrollBar();

        UpdateSortIndicators();
        UpdateColumnVisibility();
        _ = LoadDataAsync();
    }

    // ── Data loading ──────────────────────────────────────────────────────

    private async Task LoadDataAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            var models = await Task.Run(() => _repository.GetScheduleViewModels());
            _allViewModels = models;
            ApplySortAndFilter();

            Dispatcher.InvokeAsync(() =>
            {
                double todayX    = DateToX(DateTime.Today);
                double viewWidth = GanttClip.ActualWidth;
                SetGanttOffset(Math.Max(0, todayX - viewWidth / 2));
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            _ = PrefetchChildrenAsync();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ManufacturingScheduleControl.LoadDataAsync failed: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task PrefetchChildrenAsync()
    {
        try
        {
            var partIds = _allViewModels
                .Where(vm => vm.Row.PartId > 0)
                .Select(vm => vm.Row.PartId)
                .Distinct()
                .ToList();
            if (partIds.Count == 0) return;

            var orderItemIds = _allViewModels.Select(vm => vm.Row.OrderItemId).ToList();

            (var childData, var childSteps) = await Task.Run(() =>
            {
                var cd = _repository.GetAllScheduleChildItems(partIds);
                var allChildPartIds = cd.Values.SelectMany(v => v)
                    .Select(c => c.ChildPartId).Distinct().ToList();
                var cs = allChildPartIds.Count > 0 && orderItemIds.Count > 0
                    ? _repository.GetChildStepTrackers(orderItemIds, allChildPartIds)
                    : new Dictionary<(int, int), List<ScheduleStepTracker>>();
                return (cd, cs);
            });

            _childDataMap = childData;
            _childStepMap = childSteps;

            foreach (var row in _displayRows.Where(r => !r.IsChild && r.Vm != null))
            {
                int pid = row.Vm!.Row.PartId;
                row.HasChildren = pid > 0 &&
                    _childDataMap.TryGetValue(pid, out var ch) && ch.Count > 0;
            }

            Logger.Instance.Info(
                $"ManufacturingScheduleControl: prefetched children for {_childDataMap.Count} part(s), " +
                $"child steps for {_childStepMap.Count} (oi,part) pairs");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ManufacturingScheduleControl.PrefetchChildrenAsync failed: {ex.Message}", ex);
        }
    }

    // ── Search + Sort + Filter ────────────────────────────────────────────

    private void ApplySortAndFilter()
    {
        string query = SearchBox.Text.Trim();
        IEnumerable<ScheduleViewModel> source = _allViewModels;

        if (!string.IsNullOrEmpty(query))
        {
            source = source.Where(vm =>
                ContainsIgnoreCase(vm.Row.JobNumber,     query) ||
                ContainsIgnoreCase(vm.Row.DrawingNumber, query) ||
                ContainsIgnoreCase(vm.Row.Description,   query));
        }

        source = _sortColumn switch
        {
            SortColumn.Job      when _sortAscending  => source.OrderBy(vm => vm.Row.JobNumber),
            SortColumn.Job                            => source.OrderByDescending(vm => vm.Row.JobNumber),
            SortColumn.Customer when _sortAscending   => source.OrderBy(vm => vm.Row.CustomerName),
            SortColumn.Customer                       => source.OrderByDescending(vm => vm.Row.CustomerName),
            SortColumn.DueDate  when _sortAscending   => source.OrderBy(vm => vm.Row.DueDate),
            SortColumn.DueDate                        => source.OrderByDescending(vm => vm.Row.DueDate),
            _                                         => source,
        };

        _viewModels  = source.ToList();
        _displayRows = BuildDisplayRows();
        LeftRows.ItemsSource = _displayRows;

        Logger.Instance.Info(
            $"ApplySortAndFilter: query='{query}' rows={_viewModels.Count}/{_allViewModels.Count} " +
            $"display={_displayRows.Count}");

        OuterScroll.ScrollToTop();
        Render();
    }

    private List<ScheduleDisplayRow> BuildDisplayRows()
    {
        var result = new List<ScheduleDisplayRow>();
        foreach (var vm in _viewModels)
        {
            int  pid         = vm.Row.PartId;
            bool hasChildren = pid > 0 &&
                _childDataMap.TryGetValue(pid, out var ch) && ch.Count > 0;
            bool isExpanded  = _expandedOiIds.Contains(vm.Row.OrderItemId) && hasChildren;

            var parentRow = new ScheduleDisplayRow
            {
                IsChild       = false,
                Vm            = vm,
                DrawingNumber = vm.Row.DrawingNumber,
                Description   = vm.Row.Description,
                QtyText       = vm.Row.Quantity.ToString(),
            };
            parentRow.HasChildren = hasChildren;
            parentRow.IsExpanded  = isExpanded;
            parentRow.MemoText    = vm.MemoText;
            parentRow.StatusText  = vm.StatusText;
            result.Add(parentRow);

            if (!isExpanded || !_childDataMap.TryGetValue(pid, out var children)) continue;
            foreach (var child in children)
            {
                var childSteps = _childStepMap.TryGetValue(
                    (vm.Row.OrderItemId, child.ChildPartId), out var s) ? s : [];
                int completed = childSteps.Count(st => st.EndTime != null);
                var childRow = new ScheduleDisplayRow
                {
                    IsChild       = true,
                    ChildPartId   = child.ChildPartId,
                    ParentOiId    = vm.Row.OrderItemId,
                    DrawingNumber = child.DrawingNumber,
                    Description   = child.Description,
                    QtyText       = (vm.Row.Quantity * child.CumulativePathQty).ToString(),
                    Steps         = childSteps,
                };
                childRow.MemoText   = child.MemoText;
                childRow.StatusText = child.TemplateSteps > 0 ? $"{completed}/{child.TemplateSteps}" : "-";
                result.Add(childRow);
            }
        }
        return result;
    }

    private static bool ContainsIgnoreCase(string? text, string query) =>
        text != null && text.Contains(query, StringComparison.OrdinalIgnoreCase);

    // ── Column visibility ─────────────────────────────────────────────────

    private void UpdateColumnVisibility()
    {
        // Header column widths (col 0 = expand icon, always 24px)
        HeaderStrip.ColumnDefinitions[1].Width = _colCfg.ShowPo          ? new GridLength(ColWidthPo)          : new GridLength(0);
        HeaderStrip.ColumnDefinitions[3].Width = _colCfg.ShowCustomer    ? new GridLength(ColWidthCustomer)    : new GridLength(0);
        HeaderStrip.ColumnDefinitions[5].Width = _colCfg.ShowDescription ? new GridLength(ColWidthDescription) : new GridLength(0);
        HeaderStrip.ColumnDefinitions[6].Width = _colCfg.ShowQuantity    ? new GridLength(ColWidthQuantity)    : new GridLength(0);
        HeaderStrip.ColumnDefinitions[7].Width = _colCfg.ShowDueDate     ? new GridLength(ColWidthDueDate)     : new GridLength(0);
        HeaderStrip.ColumnDefinitions[8].Width = _colCfg.ShowMemo        ? new GridLength(ColWidthMemo)        : new GridLength(0);
        HeaderStrip.ColumnDefinitions[9].Width = _colCfg.ShowStatus      ? new GridLength(ColWidthStatus)      : new GridLength(0);

        // Left panel total width
        var leftWidth = new GridLength(ComputeLeftWidth());
        ContentGrid.ColumnDefinitions[0].Width       = leftWidth;
        BottomScrollStrip.ColumnDefinitions[0].Width = leftWidth;
    }

    private int ComputeLeftWidth()
    {
        int w = ColWidthExpand + ColWidthJob + ColWidthDrawing; // always visible
        if (_colCfg.ShowPo)          w += ColWidthPo;
        if (_colCfg.ShowCustomer)    w += ColWidthCustomer;
        if (_colCfg.ShowDescription) w += ColWidthDescription;
        if (_colCfg.ShowQuantity)    w += ColWidthQuantity;
        if (_colCfg.ShowDueDate)     w += ColWidthDueDate;
        if (_colCfg.ShowMemo)        w += ColWidthMemo;
        if (_colCfg.ShowStatus)      w += ColWidthStatus;
        return w;
    }

    // ── Sort indicators ───────────────────────────────────────────────────

    private void UpdateSortIndicators()
    {
        SortJobArrow.Text      = _sortColumn == SortColumn.Job      ? (_sortAscending ? "▲" : "▼") : "";
        SortCustomerArrow.Text = _sortColumn == SortColumn.Customer ? (_sortAscending ? "▲" : "▼") : "";
        SortDueDateArrow.Text  = _sortColumn == SortColumn.DueDate  ? (_sortAscending ? "▲" : "▼") : "";
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    private void ApplyViewMode()
    {
        (_pixelsPerDay, int before, int after) = _viewMode switch
        {
            GanttViewMode.Week  => (WeekViewPixelsPerDay,  WeekViewDaysBefore,  WeekViewDaysAfter),
            GanttViewMode.Month => (MonthViewPixelsPerDay, MonthViewDaysBefore, MonthViewDaysAfter),
            _                   => (DayViewPixelsPerDay,   DayViewDaysBefore,   DayViewDaysAfter),
        };

        _viewportStart = DateTime.Today.AddDays(-before);
        _totalDays     = before + after + 1;
    }

    private void Render()
    {
        double canvasWidth  = _totalDays * _pixelsPerDay;
        double canvasHeight = _displayRows.Count * RowHeight;

        GanttCanvas.Width  = canvasWidth;
        GanttCanvas.Height = canvasHeight;
        GanttCanvas.Children.Clear();
        _barHitList.Clear();

        Logger.Instance.Info(
            $"Render[sync]: rows={_displayRows.Count} canvasH={canvasHeight:F0} canvasW={canvasWidth:F0} " +
            $"ganttOffset={_ganttOffset:F0}");

        DrawRowBands(canvasWidth);
        DrawBars();
        DrawTodayLine(canvasHeight);
        DrawDueDateLines();

        // Log actual layout values after WPF completes the layout pass
        Dispatcher.InvokeAsync(() =>
        {
            DrawTimeHeader();
            UpdateGanttScrollBar();

            try
            {
                var clipPt     = GanttClip.TransformToAncestor(OuterScroll).Transform(new Point(0, 0));
                var leftRowsPt = LeftRows.TransformToAncestor(OuterScroll).Transform(new Point(0, 0));
                var canvasPt   = GanttCanvas.TransformToAncestor(OuterScroll).Transform(new Point(0, 0));

                Logger.Instance.Info(
                    $"Render[layout]: " +
                    $"OuterScroll actual=({OuterScroll.ActualWidth:F0}x{OuterScroll.ActualHeight:F0}) " +
                    $"vOffset={OuterScroll.VerticalOffset:F0} scrollH={OuterScroll.ScrollableHeight:F0} | " +
                    $"ContentGrid actual=({ContentGrid.ActualWidth:F0}x{ContentGrid.ActualHeight:F0}) | " +
                    $"GanttClip actual=({GanttClip.ActualWidth:F0}x{GanttClip.ActualHeight:F0}) pos=({clipPt.X:F0},{clipPt.Y:F0}) | " +
                    $"GanttCanvas actual=({GanttCanvas.ActualWidth:F0}x{GanttCanvas.ActualHeight:F0}) pos=({canvasPt.X:F0},{canvasPt.Y:F0}) | " +
                    $"LeftRows actual=({LeftRows.ActualWidth:F0}x{LeftRows.ActualHeight:F0}) pos=({leftRowsPt.X:F0},{leftRowsPt.Y:F0})");
            }
            catch (Exception ex)
            {
                Logger.Instance.Info($"Render[layout] error: {ex.Message}");
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DrawRowBands(double canvasWidth)
    {
        for (int i = 0; i < _displayRows.Count; i++)
        {
            double y   = i * RowHeight;
            var    row = _displayRows[i];

            SolidColorBrush fill;
            if (row.IsChild)
                fill = new SolidColorBrush(Color.FromRgb(245, 245, 255));
            else if (row.IsOverdue)
                fill = new SolidColorBrush(Color.FromArgb(60, 220, 50, 50));
            else
                fill = i % 2 == 0
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Color.FromRgb(250, 250, 250));

            var band = new Rectangle { Width = canvasWidth, Height = RowHeight, Fill = fill };
            Canvas.SetLeft(band, 0);
            Canvas.SetTop(band, y);
            GanttCanvas.Children.Add(band);

            GanttCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = canvasWidth, Y1 = y + RowHeight, Y2 = y + RowHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(238, 238, 238)), StrokeThickness = 1,
            });
        }
    }

    private void DrawBars()
    {
        for (int i = 0; i < _displayRows.Count; i++)
        {
            var    row    = _displayRows[i];
            double rowTop = i * RowHeight;
            var    steps  = row.IsChild ? row.Steps : row.Vm != null ? row.Vm.Steps : [];
            foreach (var step in steps)
                DrawStepBar(step, rowTop, i);
        }
    }

    private void DrawStepBar(ScheduleStepTracker step, double rowTop, int rowIndex)
    {
        if (!DateTime.TryParse(step.StartTime, out var startDate)) return;
        var endDate = step.EndTime != null && DateTime.TryParse(step.EndTime, out var ed)
            ? ed : DateTime.Today;

        double x1       = DateToX(startDate);
        double x2       = DateToX(endDate.AddDays(1));
        if (x2 <= x1) x2 = x1 + 2;

        double barWidth  = x2 - x1;
        double barTop    = rowTop + 3;
        double barHeight = RowHeight - 6;

        var    color   = GetShopColor(step.ShopCode);
        string tooltip = step.Description != null
            ? $"{step.ShopCode}: {step.Description}"
            : step.ShopCode;

        var bar = new Rectangle
        {
            Width   = barWidth,
            Height  = barHeight,
            Fill    = new SolidColorBrush(color),
            RadiusX = 2, RadiusY = 2,
            ToolTip = tooltip,
        };
        Canvas.SetLeft(bar, x1);
        Canvas.SetTop(bar, barTop);
        GanttCanvas.Children.Add(bar);

        _barHitList.Add((new Rect(x1, barTop, barWidth, barHeight), rowIndex, step));

        if (barWidth > 28)
        {
            var label = new TextBlock
            {
                Text              = tooltip,
                FontSize          = 9,
                Foreground        = Brushes.White,
                Padding           = new Thickness(3, 0, 0, 0),
                Width             = barWidth,
                Height            = barHeight,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip           = tooltip,
            };
            Canvas.SetLeft(label, x1);
            Canvas.SetTop(label, barTop);
            GanttCanvas.Children.Add(label);
        }
    }

    private void DrawTodayLine(double canvasHeight)
    {
        double x = DateToX(DateTime.Today);
        if (x < 0 || x > _totalDays * _pixelsPerDay) return;

        var line = new Line
        {
            X1 = x, X2 = x,
            Y1 = 0,  Y2 = canvasHeight,
            Stroke          = Brushes.Red,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
        };
        GanttCanvas.Children.Add(line);
    }

    private void DrawDueDateLines()
    {
        for (int i = 0; i < _displayRows.Count; i++)
        {
            var row = _displayRows[i];
            if (row.IsChild || row.Vm == null) continue;
            var vm = row.Vm;
            if (vm.Row.DueDate == null) continue;
            if (!DateTime.TryParse(vm.Row.DueDate, out var due)) continue;

            double x = DateToX(due.AddDays(1));
            if (x < 0 || x > _totalDays * _pixelsPerDay) continue;

            double rowTop = i * RowHeight;
            var line = new Line
            {
                X1 = x, X2 = x,
                Y1 = rowTop, Y2 = rowTop + RowHeight,
                Stroke          = new SolidColorBrush(Color.FromArgb(180, 200, 30, 30)),
                StrokeThickness = 1,
                StrokeDashArray = [3, 2],
            };
            GanttCanvas.Children.Add(line);
        }
    }

    private void SetGanttOffset(double offset)
    {
        double max = Math.Max(0, GanttCanvas.Width - GanttClip.ActualWidth);
        _ganttOffset = Math.Max(0, Math.Min(offset, max));
        GanttCanvas.RenderTransform = new TranslateTransform(-_ganttOffset, 0);
        DrawTimeHeader();
        UpdateGanttScrollBar();
    }

    private void DrawTimeHeader()
    {
        double scrollOffset = _ganttOffset;
        TimeHeaderCanvas.Children.Clear();

        switch (_viewMode)
        {
            case GanttViewMode.Month:
                DrawHeaderMonthTicks(scrollOffset);
                break;
            default:
                DrawHeaderIntervalTicks(7, "MMM d", scrollOffset);
                break;
        }

        double todayX = DateToX(DateTime.Today) - scrollOffset;
        if (todayX >= 0 && todayX <= TimeHeaderCanvas.ActualWidth)
        {
            var todayTick = new Line
            {
                X1 = todayX, X2 = todayX, Y1 = 0, Y2 = 30,
                Stroke          = Brushes.Red,
                StrokeThickness = 1.5,
            };
            TimeHeaderCanvas.Children.Add(todayTick);
        }
    }

    private void DrawHeaderIntervalTicks(int intervalDays, string format, double scrollOffset)
    {
        DateTime cursor      = _viewportStart.Date;
        int      daysToFirst = (intervalDays - (cursor.DayOfWeek == DayOfWeek.Sunday
            ? 7 : (int)cursor.DayOfWeek)) % intervalDays;
        cursor = cursor.AddDays(daysToFirst == 0 ? 0 : daysToFirst);

        double viewWidth = TimeHeaderCanvas.ActualWidth;

        while (cursor <= _viewportStart.AddDays(_totalDays))
        {
            double x = DateToX(cursor) - scrollOffset;
            if (x >= -60 && x <= viewWidth + 60)
            {
                TimeHeaderCanvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = 18, Y2 = 30,
                    Stroke          = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    StrokeThickness = 1,
                });
                var label = new TextBlock
                {
                    Text       = cursor.ToString(format),
                    FontSize   = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, 3);
                TimeHeaderCanvas.Children.Add(label);
            }
            cursor = cursor.AddDays(intervalDays);
        }
    }

    private void DrawHeaderMonthTicks(double scrollOffset)
    {
        var    cursor    = new DateTime(_viewportStart.Year, _viewportStart.Month, 1);
        double viewWidth = TimeHeaderCanvas.ActualWidth;

        while (cursor <= _viewportStart.AddDays(_totalDays))
        {
            double x = DateToX(cursor) - scrollOffset;
            if (x >= -100 && x <= viewWidth + 100)
            {
                TimeHeaderCanvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = 14, Y2 = 30,
                    Stroke          = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    StrokeThickness = 1,
                });
                var label = new TextBlock
                {
                    Text       = cursor.ToString("MMM yyyy"),
                    FontSize   = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, 2);
                TimeHeaderCanvas.Children.Add(label);
            }
            cursor = cursor.AddMonths(1);
        }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────

    private double DateToX(DateTime date) =>
        (date - _viewportStart).TotalDays * _pixelsPerDay;

    private static Color GetShopColor(string shopCode)
    {
        if (ShopColors.TryGetValue(shopCode, out var color)) return color;
        int hash = Math.Abs(shopCode.GetHashCode());
        return FallbackPalette[hash % FallbackPalette.Length];
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void FilterButton_Click(object sender, RoutedEventArgs e) =>
        FilterPopup.IsOpen = !FilterPopup.IsOpen;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplySortAndFilter();
    }

    private void HeaderJob_Click(object sender, MouseButtonEventArgs e)
    {
        _sortAscending = _sortColumn == SortColumn.Job ? !_sortAscending : true;
        _sortColumn    = SortColumn.Job;
        UpdateSortIndicators();
        ApplySortAndFilter();
    }

    private void HeaderCustomer_Click(object sender, MouseButtonEventArgs e)
    {
        _sortAscending = _sortColumn == SortColumn.Customer ? !_sortAscending : true;
        _sortColumn    = SortColumn.Customer;
        UpdateSortIndicators();
        ApplySortAndFilter();
    }

    private void HeaderDueDate_Click(object sender, MouseButtonEventArgs e)
    {
        _sortAscending = _sortColumn == SortColumn.DueDate ? !_sortAscending : true;
        _sortColumn    = SortColumn.DueDate;
        UpdateSortIndicators();
        ApplySortAndFilter();
    }

    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _viewMode = DayRadio.IsChecked  == true ? GanttViewMode.Day
                  : WeekRadio.IsChecked == true ? GanttViewMode.Week
                  : GanttViewMode.Month;

        ApplyViewMode();
        Render();

        double todayX        = DateToX(DateTime.Today);
        double viewportWidth = GanttClip.ActualWidth;
        SetGanttOffset(Math.Max(0, todayX - viewportWidth / 2));
    }

    private void GanttClip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetGanttOffset(_ganttOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void UpdateGanttScrollBar()
    {
        double scrollable           = Math.Max(0, GanttCanvas.Width - GanttClip.ActualWidth);
        GanttScrollBar.Maximum      = scrollable;
        GanttScrollBar.ViewportSize = GanttClip.ActualWidth;
        GanttScrollBar.LargeChange  = GanttClip.ActualWidth;
        GanttScrollBar.SmallChange  = 50;
        GanttScrollBar.Value        = _ganttOffset;
    }

    private void GanttScrollBar_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e) =>
        SetGanttOffset(GanttScrollBar.Value);

    private void GanttCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos      = e.GetPosition(GanttCanvas);
        int rowIndex = (int)(pos.Y / RowHeight);
        Logger.Instance.Info($"GanttCanvas click: pos=({pos.X:F0},{pos.Y:F0}) rowIndex={rowIndex} total={_displayRows.Count}");
        if (rowIndex < 0 || rowIndex >= _displayRows.Count) return;

        var displayRow = _displayRows[rowIndex];
        if (displayRow.IsChild || displayRow.Vm == null) return;

        var vm = displayRow.Vm;
        if (vm.Row.PartId <= 0)
        {
            Logger.Instance.Info($"GanttCanvas click: no part for oi={vm.Row.OrderItemId}");
            MessageBox.Show("This order item has no linked part and therefore no process template.",
                "No Part", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var steps = _repository.GetProcessTemplate(vm.Row.PartId);
        Logger.Instance.Info($"GanttCanvas click: partId={vm.Row.PartId} steps={steps.Count}");
        if (steps.Count == 0)
        {
            MessageBox.Show("No process template steps found for this part.",
                "No Steps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ScheduleStepTracker? hitStep = null;
        foreach (var (bounds, ri, step) in _barHitList)
        {
            if (ri == rowIndex && bounds.Contains(pos))
            {
                hitStep = step;
                break;
            }
        }

        DateTime? initStart = null, initEnd = null;
        if (hitStep != null)
        {
            if (DateTime.TryParse(hitStep.StartTime, out var ds)) initStart = ds;
            if (hitStep.EndTime != null && DateTime.TryParse(hitStep.EndTime, out var de)) initEnd = de;
        }

        var dialog = new StepAssignmentDialog(steps, initStart, initEnd)
        {
            Owner = Window.GetWindow(this),
        };

        if (hitStep != null)
            dialog.PreSelectStep(hitStep.ProcessTemplateId);

        if (dialog.ShowDialog() != true || dialog.Result == null) return;

        var result = dialog.Result;
        try
        {
            _repository.UpsertStepTracker(
                vm.Row.OrderItemId,
                result.ProcessTemplateId,
                result.StartDate,
                result.EndDate);

            if (result.MarkPreviousComplete)
            {
                var selected    = steps.FirstOrDefault(s => s.Id == result.ProcessTemplateId);
                var previousIds = steps
                    .Where(s => s.RowNumber < (selected?.RowNumber ?? int.MaxValue))
                    .Select(s => s.Id)
                    .ToList();
                if (previousIds.Count > 0)
                    _repository.MarkPreviousStepsComplete(vm.Row.OrderItemId, previousIds, result.StartDate);
            }

            var refreshed = _repository.GetStepTrackers(vm.Row.OrderItemId);
            var updated   = vm with { Steps = refreshed };

            int vIdx = _viewModels.FindIndex(v => v.Row.OrderItemId == vm.Row.OrderItemId);
            if (vIdx >= 0) _viewModels[vIdx] = updated;
            int allIdx = _allViewModels.FindIndex(v => v.Row.OrderItemId == vm.Row.OrderItemId);
            if (allIdx >= 0) _allViewModels[allIdx] = updated;

            _displayRows         = BuildDisplayRows();
            LeftRows.ItemsSource = _displayRows;
            Render();
            Logger.Instance.Info($"ManufacturingScheduleControl: saved step for oi={vm.Row.OrderItemId}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save step tracker:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExpandToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ScheduleDisplayRow row || row.IsChild) return;

        int oiId = row.Vm!.Row.OrderItemId;
        if (_expandedOiIds.Contains(oiId))
            _expandedOiIds.Remove(oiId);
        else
            _expandedOiIds.Add(oiId);

        double savedOffset = OuterScroll.VerticalOffset;
        _displayRows         = BuildDisplayRows();
        LeftRows.ItemsSource = _displayRows;
        Render();
        OuterScroll.ScrollToVerticalOffset(savedOffset);
    }

    private void MemoCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ScheduleDisplayRow row) return;
        if (row.PartId <= 0) return;

        var dialog = new Dialogs.PartNotesDialog(row.PartId, row.DrawingNumber ?? "—")
        {
            Owner = Window.GetWindow(this),
        };
        dialog.ShowDialog();

        if (dialog.NoteAdded)
        {
            var notes      = new Data.PartRepository().GetPartNotes(row.PartId);
            var latestMemo = notes.Count > 0 ? notes[0].Content : null;

            if (!row.IsChild)
            {
                int vIdx = _viewModels.FindIndex(v => v.Row.OrderItemId == row.OrderItemId);
                if (vIdx >= 0)
                {
                    var updated = _viewModels[vIdx] with { MemoText = latestMemo };
                    _viewModels[vIdx] = updated;
                    int allIdx = _allViewModels.FindIndex(v => v.Row.OrderItemId == row.OrderItemId);
                    if (allIdx >= 0) _allViewModels[allIdx] = updated;
                }
            }
            else
            {
                var parentVm = _allViewModels.FirstOrDefault(vm => vm.Row.OrderItemId == row.ParentOiId);
                if (parentVm != null && _childDataMap.TryGetValue(parentVm.Row.PartId, out var childList))
                {
                    int idx = childList.FindIndex(c => c.ChildPartId == row.ChildPartId);
                    if (idx >= 0)
                        childList[idx] = childList[idx] with { MemoText = latestMemo };
                }
            }

            _displayRows         = BuildDisplayRows();
            LeftRows.ItemsSource = _displayRows;
        }
        e.Handled = true;
    }

    private void DrawingNumber_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ScheduleDisplayRow row)
        {
            if (row.PartId <= 0) return;
            OpenPartRequested?.Invoke(this, (row.PartId, row.OrderItemId));
            e.Handled = true;
        }
    }
}
