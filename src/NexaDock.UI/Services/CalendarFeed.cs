using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Calendar;
using NexaDock.Core.Pulse;
using NexaDock.Core.Widgets;
using NexaDock.UI.Localization;

namespace NexaDock.UI.Services;

public sealed record AgendaItem(string Time, string Title, string? Location, bool IsNow, bool AllDay);

public sealed record AgendaDay(string Header, IReadOnlyList<AgendaItem> Items);

/// <summary>
/// Events from the user's calendar feeds (Google Calendar's secret iCal address or any .ics link),
/// shared by the calendar widgets. Refreshes every ten minutes, keeps the last good result when a
/// refresh fails, and raises a Pulse shortly before a meeting.
/// </summary>
public sealed partial class CalendarFeed : ObservableObject
{
    private const string StoreKey = "calendar.feeds";
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PulseLead = TimeSpan.FromMinutes(10);
    private const int AgendaDays = 7;
    private const int AgendaMaxEvents = 12;

    private readonly ICalendarSource _source;
    private readonly IKeyValueStore _store;
    private readonly PulseCenter _pulse;
    private readonly ILogger<CalendarFeed> _logger;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _tick;
    private readonly HashSet<string> _announced = [];
    private readonly Dictionary<string, int> _users = [];
    private List<string> _feeds;
    private IReadOnlyList<CalendarEvent> _events = [];
    private DateTimeOffset? _updated;
    private string? _error;
    private bool _started;
    private CancellationTokenSource? _loadCts;

    public CalendarFeed(ICalendarSource source, IKeyValueStore store, PulseCenter pulse, ILogger<CalendarFeed> logger)
    {
        _source = source;
        _store = store;
        _pulse = pulse;
        _logger = logger;
        _feeds = LoadFeeds();
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshEvery };
        _refresh.Tick += async (_, _) => await RefreshAsync();
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _tick.Tick += (_, _) => Present();
        IsConnected = _feeds.Count > 0;
    }

    /// <summary>Raised after events or their presentation changed (month dots, tiles).</summary>
    public event EventHandler? Changed;

    public ObservableCollection<AgendaDay> Days { get; } = [];

    public IReadOnlySet<DateOnly> EventDays { get; private set; } = new HashSet<DateOnly>();

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasNoEvents;
    [ObservableProperty] private string _inputUrl = string.Empty;
    [ObservableProperty] private string _inputMessage = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    // The next event, for tiles.
    [ObservableProperty] private bool _hasNext;
    [ObservableProperty] private bool _isNextNow;
    [ObservableProperty] private string _nextTitle = string.Empty;
    [ObservableProperty] private string _nextWhen = string.Empty;
    [ObservableProperty] private bool _hasEventsToday;

    /// <summary>The connect form is shown when nothing is connected yet, or on "Add another".</summary>
    public bool ShowConnectForm => !IsConnected || IsAdding;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ShowConnectForm));

    partial void OnIsAddingChanged(bool value) => OnPropertyChanged(nameof(ShowConnectForm));

    partial void OnInputUrlChanged(string value) => InputMessage = string.Empty;

    /// <summary>A widget that shows calendar data started; the first one starts the feed.</summary>
    public async void Attach(string widgetTypeId)
    {
        _users[widgetTypeId] = _users.GetValueOrDefault(widgetTypeId) + 1;
        if (_started)
        {
            return;
        }

        _started = true;
        _tick.Start();
        _refresh.Start();
        Present();
        await RefreshAsync();
    }

    public void Detach(string widgetTypeId)
    {
        if (_users.TryGetValue(widgetTypeId, out var n))
        {
            if (n <= 1)
            {
                _users.Remove(widgetTypeId);
            }
            else
            {
                _users[widgetTypeId] = n - 1;
            }
        }

        if (_users.Count == 0 && _started)
        {
            _started = false;
            _tick.Stop();
            _refresh.Stop();
            ClearPulses();
        }
    }

    /// <summary>The card opened: show fresh data if the last refresh is more than a minute old.</summary>
    public async void OnCardOpened()
    {
        IsAdding = false;
        Present();
        if (_updated is null || DateTimeOffset.Now - _updated > TimeSpan.FromMinutes(1))
        {
            await RefreshAsync();
        }
    }

    public void OnLanguageChanged() => Present();

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task Connect()
    {
        if (!CalendarAgenda.TryNormalizeFeedUrl(InputUrl, out var url))
        {
            InputMessage = Loc.T("calendar.invalidUrl");
            return;
        }

        if (_feeds.Contains(url, StringComparer.Ordinal))
        {
            InputMessage = Loc.T("calendar.alreadyAdded");
            return;
        }

        // Try it first, so a wrong link is reported here rather than silently saved.
        IsLoading = true;
        InputMessage = Loc.T("calendar.checking");
        CalendarFetchResult result;
        try
        {
            var (from, to) = Window();
            result = await _source.FetchAsync([url], from, to, CancellationToken.None);
        }
        finally
        {
            IsLoading = false;
        }

        if (result.FailedFeeds > 0)
        {
            InputMessage = Loc.F("calendar.connectFailed", result.Error ?? "?");
            return;
        }

        _feeds.Add(url);
        SaveFeeds();
        _logger.LogInformation("Calendar feed added ({Host}); {Count} feed(s) connected.", CalendarAgenda.DescribeFeed(url), _feeds.Count);
        InputUrl = string.Empty;
        InputMessage = CalendarAgenda.IsGooglePublicAddress(url) ? Loc.T("calendar.publicWarning") : string.Empty;
        IsConnected = true;
        IsAdding = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private void AddAnother()
    {
        InputUrl = string.Empty;
        InputMessage = string.Empty;
        IsAdding = true;
    }

    [RelayCommand]
    private void CancelAdding() => IsAdding = false;

    [RelayCommand]
    private void Disconnect()
    {
        _feeds.Clear();
        SaveFeeds();
        _logger.LogInformation("Calendar feeds removed.");
        _events = [];
        _updated = null;
        _error = null;
        IsConnected = false;
        IsAdding = false;
        ClearPulses();
        Present();
    }

    [RelayCommand]
    private static void OpenGoogleSettings() => OpenUrl("https://calendar.google.com/calendar/r/settings");

    [RelayCommand]
    private static void OpenGoogleCalendar() => OpenUrl("https://calendar.google.com/calendar/r");

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser registered: nothing useful to do.
        }
    }

    private async Task RefreshAsync()
    {
        if (_feeds.Count == 0)
        {
            Present();
            return;
        }

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        try
        {
            var (from, to) = Window();
            var feeds = _feeds.ToList();
            var result = await Task.Run(() => _source.FetchAsync(feeds, from, to, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (result.FailedFeeds < feeds.Count)
            {
                _events = result.Events;
                _updated = DateTimeOffset.Now;
            }

            _error = result.FailedFeeds > 0 ? result.Error ?? "?" : null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _error = ex.GetType().Name;
            _logger.LogWarning(ex, "Calendar refresh failed.");
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }

        Present();
    }

    /// <summary>From the start of last month (for the month grid) to six weeks ahead.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) Window()
    {
        var today = DateTime.Today;
        var from = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        return (new DateTimeOffset(from), new DateTimeOffset(today.AddDays(42)));
    }

    private void Present()
    {
        var culture = Loc.Instance.Culture;
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(now.DateTime);

        EventDays = CalendarAgenda.DaysWithEvents(_events, zone);

        Days.Clear();
        foreach (var (day, events) in CalendarAgenda.Upcoming(_events, now, zone, AgendaDays, AgendaMaxEvents))
        {
            var items = events.Select(e => new AgendaItem(
                FormatTime(e, day, culture),
                e.Title,
                e.Location,
                !e.AllDay && e.Start <= now && e.End > now,
                e.AllDay)).ToList();
            Days.Add(new AgendaDay(DayHeader(day, today, culture), items));
        }

        HasNoEvents = IsConnected && Days.Count == 0 && _updated is not null;
        HasEventsToday = CalendarAgenda.ForDay(_events, today, zone).Any(e => e.End > now);

        var next = CalendarAgenda.Next(_events, now);
        HasNext = next is not null;
        if (next is not null)
        {
            NextTitle = next.Title;
            IsNextNow = next.Start <= now;
            NextWhen = IsNextNow
                ? Loc.F("calendar.nowUntil", next.End.ToLocalTime().ToString("t", culture))
                : When(next.Start, now, culture);
        }
        else
        {
            IsNextNow = false;
            NextTitle = IsConnected ? Loc.T("calendar.noUpcoming") : Loc.T("calendar.title");
            NextWhen = IsConnected ? string.Empty : Loc.T("calendar.notConnected");
        }

        StatusText = !IsConnected
            ? string.Empty
            : _updated is { } u
                ? Loc.F("calendar.connected", _feeds.Count, u.ToString("t", culture))
                : Loc.T("calendar.loading");
        ErrorText = IsConnected && _error is not null ? Loc.F("calendar.error", _error) : string.Empty;

        UpdatePulse(now, culture);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatTime(CalendarEvent e, DateOnly day, CultureInfo culture)
    {
        if (e.AllDay)
        {
            return Loc.T("calendar.allDay");
        }

        var start = e.Start.ToLocalTime();
        var end = e.End.ToLocalTime();
        var startText = DateOnly.FromDateTime(start.DateTime) < day ? "…" : start.ToString("t", culture);
        var endText = DateOnly.FromDateTime(end.DateTime) > day ? "…" : end.ToString("t", culture);
        return e.End == e.Start ? startText : $"{startText} – {endText}";
    }

    private static string DayHeader(DateOnly day, DateOnly today, CultureInfo culture)
    {
        if (day == today)
        {
            return Loc.T("calendar.today");
        }

        if (day == today.AddDays(1))
        {
            return Loc.T("calendar.tomorrow");
        }

        var text = day.ToDateTime(TimeOnly.MinValue).ToString("dddd, d. MMMM", culture);
        return culture.TextInfo.ToUpper(text[0]) + text[1..];
    }

    private static string When(DateTimeOffset start, DateTimeOffset now, CultureInfo culture)
    {
        var left = start - now;
        var local = start.ToLocalTime();
        if (left <= TimeSpan.FromMinutes(60))
        {
            return Loc.F("calendar.inTime", Loc.F("common.minutes", Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))));
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        var time = local.ToString("t", culture);
        if (day == today)
        {
            return time;
        }

        var dayText = day == today.AddDays(1)
            ? Loc.T("calendar.tomorrow")
            : local.ToString("ddd d.", culture);
        return Loc.F("calendar.dayAt", dayText, time);
    }

    /// <summary>
    /// A Pulse for each timed event starting within ten minutes. Each is raised once: after it is
    /// dismissed or acknowledged it stays quiet, and it is cleared when the event begins.
    /// </summary>
    private void UpdatePulse(DateTimeOffset now, CultureInfo culture)
    {
        var target = _users.ContainsKey(WidgetTypes.Agenda) ? WidgetTypes.Agenda
            : _users.ContainsKey(WidgetTypes.Calendar) ? WidgetTypes.Calendar
            : WidgetTypes.Clock;
        var soon = _events
            .Where(e => !e.AllDay && e.Start > now && e.Start - now <= PulseLead)
            .ToDictionary(e => "calendar:" + e.Key);

        foreach (var alert in _pulse.Alerts.Where(a => a.Id.StartsWith("calendar:", StringComparison.Ordinal) && !soon.ContainsKey(a.Id)).ToList())
        {
            _pulse.Clear(alert.Id);
        }

        foreach (var (id, e) in soon)
        {
            var showing = _pulse.Alerts.Any(a => a.Id == id);
            if (_announced.Contains(id) && !showing)
            {
                continue;
            }

            _announced.Add(id);
            var minutes = Math.Max(1, (int)Math.Ceiling((e.Start - now).TotalMinutes));
            _pulse.Raise(new PulseAlert(id, target, PulseSeverity.Info, "pulse.meeting",
                [e.Title, minutes.ToString(CultureInfo.InvariantCulture)], 0xE787));
        }

        if (_announced.Count > 200)
        {
            _announced.Clear();
        }
    }

    private void ClearPulses()
    {
        foreach (var alert in _pulse.Alerts.Where(a => a.Id.StartsWith("calendar:", StringComparison.Ordinal)).ToList())
        {
            _pulse.Clear(alert.Id);
        }
    }

    private List<string> LoadFeeds()
    {
        try
        {
            var json = _store.Get(StoreKey);
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveFeeds() => _store.Set(StoreKey, _feeds.Count == 0 ? null : JsonSerializer.Serialize(_feeds));
}
