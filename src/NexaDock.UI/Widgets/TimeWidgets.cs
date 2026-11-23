using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;

namespace NexaDock.UI.Widgets;

/// <summary>Shared one-second tick aligned to wall-clock seconds.</summary>
internal static class ClockTicker
{
    private static DispatcherTimer? _timer;

    public static event EventHandler? Tick;

    public static void Subscribe(EventHandler handler)
    {
        Tick += handler;
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            var last = -1;
            _timer.Tick += (_, _) =>
            {
                var second = DateTime.Now.Second;
                if (second != last)
                {
                    last = second;
                    Tick?.Invoke(null, EventArgs.Empty);
                }
            };
            _timer.Start();
        }
    }

    public static void Unsubscribe(EventHandler handler) => Tick -= handler;
}

public sealed partial class ClockWidget : WidgetViewModel
{
    private readonly IShellCommands _shell;
    private bool _use24;

    public ClockWidget(WidgetPlacement placement, IShellCommands shell)
        : base(placement)
    {
        _shell = shell;
        Calendar = new MonthCalendar();
    }

    public override bool HasCard => true;

    public MonthCalendar Calendar { get; }

    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty] private string _meridiem = string.Empty;
    [ObservableProperty] private string _date = string.Empty;
    [ObservableProperty] private string _longTime = string.Empty;
    [ObservableProperty] private string _longDate = string.Empty;
    [ObservableProperty] private string _weekText = string.Empty;

    public override void ApplySettings(DockSettings settings)
    {
        _use24 = settings.Use24HourClock;
        Update();
    }

    public override void Start()
    {
        ClockTicker.Subscribe(OnTick);
        Update();
    }

    private void OnTick(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        Time = _use24 ? now.ToString("HH:mm", culture) : now.ToString("h:mm", CultureInfo.InvariantCulture);
        Meridiem = _use24 ? string.Empty : now.ToString("tt", CultureInfo.InvariantCulture);
        Date = now.ToString("ddd d MMM", culture);
        LongTime = _use24 ? now.ToString("HH:mm:ss", culture) : now.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);
        LongDate = now.ToString("D", culture);
        WeekText = $"Week {ISOWeek.GetWeekOfYear(now)} · day {now.DayOfYear} of {(DateTime.IsLeapYear(now.Year) ? 366 : 365)}";
        if (Calendar.Today != DateOnly.FromDateTime(now))
        {
            Calendar.GoToToday();
        }
    }

    [RelayCommand]
    private void OpenDateTimeSettings() => _shell.OpenDateTimeSettings();

    public override void OnCardOpened() => Calendar.GoToToday();

    public override void Dispose()
    {
        ClockTicker.Unsubscribe(OnTick);
        base.Dispose();
    }
}

/// <summary>"Calendar page" tile: weekday over the day number.</summary>
public sealed partial class CalendarWidget : WidgetViewModel
{
    public CalendarWidget(WidgetPlacement placement)
        : base(placement) => Calendar = new MonthCalendar();

    public override bool HasCard => true;

    public MonthCalendar Calendar { get; }

    [ObservableProperty] private string _weekday = string.Empty;
    [ObservableProperty] private string _day = string.Empty;
    [ObservableProperty] private string _month = string.Empty;

    public override void Start()
    {
        ClockTicker.Subscribe(OnTick);
        Update();
    }

    private void OnTick(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var now = DateTime.Now;
        Weekday = now.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant();
        Day = now.Day.ToString(CultureInfo.CurrentCulture);
        Month = now.ToString("MMMM", CultureInfo.CurrentCulture);
        if (Calendar.Today != DateOnly.FromDateTime(now))
        {
            Calendar.GoToToday();
        }
    }

    public override void OnCardOpened() => Calendar.GoToToday();

    public override void Dispose()
    {
        ClockTicker.Unsubscribe(OnTick);
        base.Dispose();
    }
}

/// <summary>A navigable month grid (Monday-first per ISO, or the culture's first day).</summary>
public sealed partial class MonthCalendar : ObservableObject
{
    private DateOnly _shown;

    public MonthCalendar() => GoToToday();

    public DateOnly Today { get; private set; }

    public ObservableCollection<CalendarDay> Days { get; } = [];

    public IReadOnlyList<string> WeekdayNames { get; private set; } = [];

    [ObservableProperty] private string _title = string.Empty;

    [RelayCommand]
    public void GoToToday()
    {
        Today = DateOnly.FromDateTime(DateTime.Now);
        _shown = new DateOnly(Today.Year, Today.Month, 1);
        Build();
    }

    [RelayCommand]
    private void Previous()
    {
        _shown = _shown.AddMonths(-1);
        Build();
    }

    [RelayCommand]
    private void Next()
    {
        _shown = _shown.AddMonths(1);
        Build();
    }

    private void Build()
    {
        var culture = CultureInfo.CurrentCulture;
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        Title = _shown.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", culture);
        WeekdayNames = Enumerable.Range(0, 7)
            .Select(i => culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7)))
            .ToList();
        OnPropertyChanged(nameof(WeekdayNames));

        var offset = ((int)_shown.DayOfWeek - (int)first + 7) % 7;
        var start = _shown.AddDays(-offset);
        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            Days.Add(new CalendarDay(d.Day.ToString(culture), d == Today, d.Month == _shown.Month, d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        }
    }
}

public sealed record CalendarDay(string Text, bool IsToday, bool IsInMonth, bool IsWeekend);

/// <summary>How much of the day, week, month and year has passed.</summary>
public sealed partial class DayProgressWidget : WidgetViewModel
{
    public DayProgressWidget(WidgetPlacement placement)
        : base(placement)
    {
    }

    public override bool HasCard => true;

    [ObservableProperty] private double _day;
    [ObservableProperty] private double _week;
    [ObservableProperty] private double _month;
    [ObservableProperty] private double _year;
    [ObservableProperty] private string _dayText = string.Empty;
    [ObservableProperty] private string _remainingText = string.Empty;

    public override void Start()
    {
        ClockTicker.Subscribe(OnTick);
        Update();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (DateTime.Now.Second % 10 == 0)
        {
            Update();
        }
    }

    private void Update()
    {
        var now = DateTime.Now;
        Day = now.TimeOfDay.TotalMinutes / (24 * 60) * 100;
        var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        Week = (now - weekStart).TotalHours / (7 * 24) * 100;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        Month = (now - monthStart).TotalHours / (DateTime.DaysInMonth(now.Year, now.Month) * 24) * 100;
        var yearStart = new DateTime(now.Year, 1, 1);
        Year = (now - yearStart).TotalHours / ((DateTime.IsLeapYear(now.Year) ? 366 : 365) * 24) * 100;
        DayText = $"{Day:0}%";
        var left = now.Date.AddDays(1) - now;
        RemainingText = $"{(int)left.TotalHours} h {left.Minutes} min left today";
    }

    public override void Dispose()
    {
        ClockTicker.Unsubscribe(OnTick);
        base.Dispose();
    }
}
