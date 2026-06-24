/// <summary>
/// ManufacturingScheduleControl.xaml.cs
/// Gantt-style Manufacturing Schedule page. Left panel lists all order items from
/// active POs; right Canvas displays coloured step-tracker bars per row.
/// </summary>

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

        _viewModels          = source.ToList();
        LeftRows.ItemsSource = _viewModels;

        Logger.Instance.Info(
            $"ApplySortAndFilter: query='{query}' rows={_viewModels.Count}/{_allViewModels.Count} " +
            $"OuterScroll.VerticalOffset={OuterScroll.VerticalOffset:F0} " +
            $"OuterScroll.ScrollableHeight={OuterScroll.ScrollableHeight:F0}");

        OuterScroll.ScrollToTop();

        Logger.Instance.Info(
            $"ApplySortAndFilter: after ScrollToTop VerticalOffset={OuterScroll.VerticalOffset:F0}");

        Render();
    }

    private static bool ContainsIgnoreCase(string? text, string query) =>
        text != null && text.Contains(query, StringComparison.OrdinalIgnoreCase);

    // ── Column visibility ─────────────────────────────────────────────────

    private void UpdateColumnVisibility()
    {
        // Header column widths
        HeaderStrip.ColumnDefinitions[0].Width = _colCfg.ShowPo          ? new GridLength(ColWidthPo)          : new GridLength(0);
        HeaderStrip.ColumnDefinitions[2].Width = _colCfg.ShowCustomer    ? new GridLength(ColWidthCustomer)    : new GridLength(0);
        HeaderStrip.ColumnDefinitions[4].Width = _colCfg.ShowDescription ? new GridLength(ColWidthDescription) : new GridLength(0);
        HeaderStrip.ColumnDefinitions[5].Width = _colCfg.ShowQuantity    ? new GridLength(ColWidthQuantity)    : new GridLength(0);
        HeaderStrip.ColumnDefinitions[6].Width = _colCfg.ShowDueDate     ? new GridLength(ColWidthDueDate)     : new GridLength(0);
        HeaderStrip.ColumnDefinitions[7].Width = _colCfg.ShowMemo        ? new GridLength(ColWidthMemo)        : new GridLength(0);
        HeaderStrip.ColumnDefinitions[8].Width = _colCfg.ShowStatus      ? new GridLength(ColWidthStatus)      : new GridLength(0);

        // Left panel total width
        var leftWidth = new GridLength(ComputeLeftWidth());
        ContentGrid.ColumnDefinitions[0].Width       = leftWidth;
        BottomScrollStrip.ColumnDefinitions[0].Width = leftWidth;
    }

    private int ComputeLeftWidth()
    {
        int w = ColWidthJob + ColWidthDrawing; // always visible
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
        double canvasHeight = _viewModels.Count * RowHeight;

        GanttCanvas.Width  = canvasWidth;
        GanttCanvas.Height = canvasHeight;
        GanttCanvas.Children.Clear();
        _barHitList.Clear();

        Logger.Instance.Info(
            $"Render[sync]: rows={_viewModels.Count} canvasH={canvasHeight:F0} canvasW={canvasWidth:F0} " +
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
        for (int i = 0; i < _viewModels.Count; i++)
        {
            double y    = i * RowHeight;
            var    band = new Rectangle
            {
                Width  = canvasWidth,
                Height = RowHeight,
                Fill   = i % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(250, 250, 250)),
            };
            if (_viewModels[i].IsOverdue)
                band.Fill = new SolidColorBrush(Color.FromArgb(60, 220, 50, 50));

            Canvas.SetLeft(band, 0);
            Canvas.SetTop(band, y);
            GanttCanvas.Children.Add(band);

            var line = new Line
            {
                X1 = 0, X2 = canvasWidth,
                Y1 = y + RowHeight, Y2 = y + RowHeight,
                Stroke          = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                StrokeThickness = 1,
            };
            GanttCanvas.Children.Add(line);
        }
    }

    private void DrawBars()
    {
        for (int i = 0; i < _viewModels.Count; i++)
        {
            var    vm     = _viewModels[i];
            double rowTop = i * RowHeight;

            foreach (var step in vm.Steps)
            {
                if (!DateTime.TryParse(step.StartTime, out var startDate)) continue;
                var endDate = step.EndTime != null && DateTime.TryParse(step.EndTime, out var ed)
                    ? ed : DateTime.Today;

                double x1       = DateToX(startDate);
                double x2       = DateToX(endDate.AddDays(1));
                if (x2 <= x1) x2 = x1 + 2;

                double barWidth  = x2 - x1;
                double barTop    = rowTop + 3;
                double barHeight = RowHeight - 6;

                var color       = GetShopColor(step.ShopCode);
                string tooltip  = step.Description != null
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

                _barHitList.Add((new Rect(x1, barTop, barWidth, barHeight), i, step));

                if (barWidth > 28)
                {
                    var label = new TextBlock
                    {
                        Text             = tooltip,
                        FontSize         = 9,
                        Foreground       = Brushes.White,
                        Padding          = new Thickness(3, 0, 0, 0),
                        Width            = barWidth,
                        Height           = barHeight,
                        TextTrimming     = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip          = tooltip,
                    };
                    Canvas.SetLeft(label, x1);
                    Canvas.SetTop(label, barTop);
                    GanttCanvas.Children.Add(label);
                }
            }
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
        for (int i = 0; i < _viewModels.Count; i++)
        {
            var vm = _viewModels[i];
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
        Logger.Instance.Info($"GanttCanvas click: pos=({pos.X:F0},{pos.Y:F0}) rowIndex={rowIndex} total={_viewModels.Count}");
        if (rowIndex < 0 || rowIndex >= _viewModels.Count) return;

        var vm = _viewModels[rowIndex];
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
            _viewModels[rowIndex] = updated;

            int allIdx = _allViewModels.FindIndex(v => v.Row.OrderItemId == vm.Row.OrderItemId);
            if (allIdx >= 0) _allViewModels[allIdx] = updated;

            LeftRows.ItemsSource = null;
            LeftRows.ItemsSource = _viewModels;
            Render();
            Logger.Instance.Info($"ManufacturingScheduleControl: saved step for oi={vm.Row.OrderItemId}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save step tracker:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MemoCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ScheduleViewModel vm) return;
        if (vm.Row.PartId <= 0) return;

        var dialog = new Dialogs.PartNotesDialog(vm.Row.PartId, vm.Row.DrawingNumber ?? "—")
        {
            Owner = Window.GetWindow(this),
        };
        dialog.ShowDialog();

        if (dialog.NoteAdded)
        {
            var notes      = new Data.PartRepository().GetPartNotes(vm.Row.PartId);
            var latestMemo = notes.Count > 0 ? notes[0].Content : null;
            int idx        = _viewModels.IndexOf(vm);
            if (idx >= 0)
            {
                var updated      = vm with { MemoText = latestMemo };
                _viewModels[idx] = updated;

                int allIdx = _allViewModels.FindIndex(v => v.Row.OrderItemId == vm.Row.OrderItemId);
                if (allIdx >= 0) _allViewModels[allIdx] = updated;

                LeftRows.ItemsSource = null;
                LeftRows.ItemsSource = _viewModels;
            }
        }
        e.Handled = true;
    }

    private void DrawingNumber_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ScheduleViewModel vm)
        {
            if (vm.Row.PartId <= 0) return;
            OpenPartRequested?.Invoke(this, (vm.Row.PartId, vm.Row.OrderItemId));
            e.Handled = true;
        }
    }
}
