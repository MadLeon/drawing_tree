/// <summary>
/// ManufacturingScheduleControl.xaml.cs
/// Gantt-style Manufacturing Schedule page. Left panel lists all order items from
/// active POs; right Canvas displays coloured step-tracker bars per row.
/// </summary>

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
using Rectangle        = System.Windows.Shapes.Rectangle;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;

using UserControl = System.Windows.Controls.UserControl;

namespace DrawingTree.Controls;

public partial class ManufacturingScheduleControl : UserControl
{
    // ── Configuration ──────────────────────────────────────────────────────

    private const int RowHeight = 32;

    // Day view: 30px/day, ±30 days around today
    private const double DayViewPixelsPerDay   = 30.0;
    private const int    DayViewDaysBefore     = 30;
    private const int    DayViewDaysAfter      = 90;

    // Week view: ~20px/day (140px/week)
    private const double WeekViewPixelsPerDay  = 20.0;
    private const int    WeekViewDaysBefore    = 56;  // 8 weeks
    private const int    WeekViewDaysAfter     = 112; // 16 weeks

    // Month view: ~4px/day (≈120px/month)
    private const double MonthViewPixelsPerDay = 4.0;
    private const int    MonthViewDaysBefore   = 90;  // 3 months
    private const int    MonthViewDaysAfter    = 275; // ~9 months

    // Shop code → bar colour palette
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

    // ── State ──────────────────────────────────────────────────────────────

    private readonly ScheduleRepository _repository = new();
    private List<ScheduleViewModel>     _viewModels = [];

    private enum GanttViewMode { Day, Week, Month }
    private GanttViewMode _viewMode      = GanttViewMode.Day;
    private DateTime      _viewportStart;
    private double        _pixelsPerDay;
    private int           _totalDays;

    // Bars drawn on the canvas: (rect bounds, vm index, step)
    private readonly List<(Rect Bounds, int RowIndex, ScheduleStepTracker Step)> _barHitList = [];

    public event EventHandler? BackRequested;
    public event EventHandler<(int PartId, int OrderItemId)>? OpenPartRequested;

    // ── Construction ──────────────────────────────────────────────────────

    public ManufacturingScheduleControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        TimeHeaderCanvas.SizeChanged += (_, _) => DrawTimeHeader();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyViewMode();
        LoadData();

        // OuterScroll (and GanttHScroll) can mark PreviewMouseLeftButtonDown as Handled,
        // which suppresses the normal bubbling MouseLeftButtonDown on the canvas.
        // Registering with handledEventsToo:true ensures the click fires regardless.
        GanttHScroll.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(GanttCanvas_MouseLeftButtonDown),
            handledEventsToo: true);
    }

    // ── Data loading ──────────────────────────────────────────────────────

    private void LoadData()
    {
        _viewModels = _repository.GetScheduleViewModels();
        LeftRows.ItemsSource = _viewModels;
        Logger.Instance.Info($"ManufacturingScheduleControl: loaded {_viewModels.Count} rows");
        Render();

        // Scroll right panel so today is centered after layout
        Dispatcher.InvokeAsync(() =>
        {
            double todayX      = DateToX(DateTime.Today);
            double viewWidth   = GanttHScroll.ViewportWidth;
            GanttHScroll.ScrollToHorizontalOffset(Math.Max(0, todayX - viewWidth / 2));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
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

        DrawRowBands(canvasWidth);
        DrawBars();
        DrawTodayLine(canvasHeight);
        DrawDueDateLines();

        // Time header is redrawn via Dispatcher to ensure ActualWidth is available
        Dispatcher.InvokeAsync(DrawTimeHeader, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Draws alternating row bands on the Gantt canvas.</summary>
    private void DrawRowBands(double canvasWidth)
    {
        for (int i = 0; i < _viewModels.Count; i++)
        {
            double y = i * RowHeight;
            var band = new Rectangle
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

            // Row separator
            var line = new Line
            {
                X1 = 0, X2 = canvasWidth,
                Y1 = y + RowHeight, Y2 = y + RowHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                StrokeThickness = 1,
            };
            GanttCanvas.Children.Add(line);
        }
    }

    /// <summary>Draws step-tracker bars for every row.</summary>
    private void DrawBars()
    {
        for (int i = 0; i < _viewModels.Count; i++)
        {
            var vm = _viewModels[i];
            double rowTop = i * RowHeight;

            foreach (var step in vm.Steps)
            {
                if (!DateTime.TryParse(step.StartTime, out var startDate)) continue;
                var endDate = step.EndTime != null && DateTime.TryParse(step.EndTime, out var ed)
                    ? ed : DateTime.Today;

                double x1 = DateToX(startDate);
                double x2 = DateToX(endDate.AddDays(1)); // inclusive
                if (x2 <= x1) x2 = x1 + 2;

                double barWidth = x2 - x1;
                double barTop   = rowTop + 3;
                double barHeight= RowHeight - 6;

                var color  = GetShopColor(step.ShopCode);
                var brush  = new SolidColorBrush(color);
                string tooltipText = step.Description != null
                    ? $"{step.ShopCode}: {step.Description}"
                    : step.ShopCode;
                var border = new Rectangle
                {
                    Width  = barWidth,
                    Height = barHeight,
                    Fill   = brush,
                    RadiusX = 2, RadiusY = 2,
                    ToolTip = tooltipText,
                };
                Canvas.SetLeft(border, x1);
                Canvas.SetTop(border, barTop);
                GanttCanvas.Children.Add(border);

                _barHitList.Add((new Rect(x1, barTop, barWidth, barHeight), i, step));

                if (barWidth > 28)
                {
                    string labelText = step.Description != null
                        ? $"{step.ShopCode}: {step.Description}"
                        : step.ShopCode;
                    var label = new TextBlock
                    {
                        Text       = labelText,
                        FontSize   = 9,
                        Foreground = Brushes.White,
                        Padding    = new Thickness(3, 0, 0, 0),
                        Width      = barWidth,
                        Height     = barHeight,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Canvas.SetLeft(label, x1);
                    Canvas.SetTop(label, barTop);
                    GanttCanvas.Children.Add(label);
                }
            }
        }
    }

    /// <summary>Draws the red vertical line representing today.</summary>
    private void DrawTodayLine(double canvasHeight)
    {
        double x = DateToX(DateTime.Today);
        if (x < 0 || x > _totalDays * _pixelsPerDay) return;

        var line = new Line
        {
            X1 = x, X2 = x,
            Y1 = 0,  Y2 = canvasHeight,
            Stroke = Brushes.Red,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
        };
        GanttCanvas.Children.Add(line);
    }

    /// <summary>Draws a dashed vertical line at each row's due date (when visible).</summary>
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
                Stroke = new SolidColorBrush(Color.FromArgb(180, 200, 30, 30)),
                StrokeThickness = 1,
                StrokeDashArray = [3, 2],
            };
            GanttCanvas.Children.Add(line);
        }
    }

    /// <summary>
    /// Redraws the time scale header canvas using the current horizontal scroll offset
    /// so that visible tick marks align with the Gantt canvas below.
    /// </summary>
    private void DrawTimeHeader()
    {
        double scrollOffset = GanttHScroll.HorizontalOffset;
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

        // Today marker on header
        double todayX = DateToX(DateTime.Today) - scrollOffset;
        if (todayX >= 0 && todayX <= TimeHeaderCanvas.ActualWidth)
        {
            var todayTick = new Line
            {
                X1 = todayX, X2 = todayX, Y1 = 0, Y2 = 30,
                Stroke = Brushes.Red,
                StrokeThickness = 1.5,
            };
            TimeHeaderCanvas.Children.Add(todayTick);
        }
    }

    private void DrawHeaderIntervalTicks(int intervalDays, string format, double scrollOffset)
    {
        DateTime cursor = _viewportStart.Date;
        // Advance to the first Monday (or first day of interval)
        int daysToFirst = (intervalDays - (cursor.DayOfWeek == DayOfWeek.Sunday
            ? 7 : (int)cursor.DayOfWeek)) % intervalDays;
        cursor = cursor.AddDays(daysToFirst == 0 ? 0 : daysToFirst);

        double viewWidth = TimeHeaderCanvas.ActualWidth;

        while (cursor <= _viewportStart.AddDays(_totalDays))
        {
            double x = DateToX(cursor) - scrollOffset;
            if (x >= -60 && x <= viewWidth + 60)
            {
                var tick = new Line
                {
                    X1 = x, X2 = x, Y1 = 18, Y2 = 30,
                    Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    StrokeThickness = 1,
                };
                TimeHeaderCanvas.Children.Add(tick);

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
        var cursor    = new DateTime(_viewportStart.Year, _viewportStart.Month, 1);
        double viewWidth = TimeHeaderCanvas.ActualWidth;

        while (cursor <= _viewportStart.AddDays(_totalDays))
        {
            double x = DateToX(cursor) - scrollOffset;
            if (x >= -100 && x <= viewWidth + 100)
            {
                var tick = new Line
                {
                    X1 = x, X2 = x, Y1 = 14, Y2 = 30,
                    Stroke = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    StrokeThickness = 1,
                };
                TimeHeaderCanvas.Children.Add(tick);

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

    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _viewMode = DayRadio.IsChecked == true   ? GanttViewMode.Day
                  : WeekRadio.IsChecked == true  ? GanttViewMode.Week
                  : GanttViewMode.Month;

        ApplyViewMode();
        Render();

        // Scroll to today
        double todayX = DateToX(DateTime.Today);
        double viewportWidth = GanttHScroll.ViewportWidth;
        GanttHScroll.ScrollToHorizontalOffset(Math.Max(0, todayX - viewportWidth / 2));
    }

    /// <summary>
    /// Intercepts mouse wheel over the Gantt panel and converts it to horizontal scroll.
    /// Marks the event handled so the outer vertical ScrollViewer is not also triggered.
    /// </summary>
    private void GanttHScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        GanttHScroll.ScrollToHorizontalOffset(GanttHScroll.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    /// <summary>
    /// Redraws the time header whenever the Gantt canvas scrolls horizontally,
    /// and keeps the external horizontal ScrollBar in sync.
    /// </summary>
    private void GanttHScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0)
            DrawTimeHeader();
        UpdateGanttScrollBar();
    }

    /// <summary>Syncs the external horizontal ScrollBar with GanttHScroll state.</summary>
    private void UpdateGanttScrollBar()
    {
        GanttScrollBar.Maximum      = GanttHScroll.ScrollableWidth;
        GanttScrollBar.ViewportSize = GanttHScroll.ViewportWidth;
        GanttScrollBar.LargeChange  = GanttHScroll.ViewportWidth;
        GanttScrollBar.SmallChange  = 50;
        GanttScrollBar.Value        = GanttHScroll.HorizontalOffset;
    }

    /// <summary>Scrolls the Gantt panel when the external ScrollBar is dragged.</summary>
    private void GanttScrollBar_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        GanttHScroll.ScrollToHorizontalOffset(GanttScrollBar.Value);
    }

    /// <summary>
    /// Click on the Gantt canvas: determine row, hit-test bars, open assignment dialog.
    /// </summary>
    private void GanttCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(GanttCanvas);
        int rowIndex = (int)(pos.Y / RowHeight);
        Logger.Instance.Debug($"GanttCanvas click: pos=({pos.X:F0},{pos.Y:F0}) rowIndex={rowIndex} total={_viewModels.Count}");
        if (rowIndex < 0 || rowIndex >= _viewModels.Count) return;

        var vm = _viewModels[rowIndex];
        if (vm.Row.PartId <= 0)
        {
            Logger.Instance.Debug($"GanttCanvas click: no part for oi={vm.Row.OrderItemId}");
            MessageBox.Show("This order item has no linked part and therefore no process template.",
                "No Part", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var steps = _repository.GetProcessTemplate(vm.Row.PartId);
        Logger.Instance.Debug($"GanttCanvas click: partId={vm.Row.PartId} steps={steps.Count}");
        if (steps.Count == 0)
        {
            MessageBox.Show("No process template steps found for this part.",
                "No Steps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Hit-test existing bars to pre-populate dates for editing
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

            // Refresh only this row's steps
            var refreshed = _repository.GetStepTrackers(vm.Row.OrderItemId);
            var updated = vm with { Steps = refreshed };
            _viewModels[rowIndex] = updated;

            // Refresh binding for this row
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

    /// <summary>Opens the notes dialog for the clicked row's part.</summary>
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
                _viewModels[idx] = vm with { MemoText = latestMemo };
                LeftRows.ItemsSource = null;
                LeftRows.ItemsSource = _viewModels;
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// Navigates to the Part detail page for the clicked drawing number row.
    /// Fires OpenPartRequested only when the row has a linked part.
    /// </summary>
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
