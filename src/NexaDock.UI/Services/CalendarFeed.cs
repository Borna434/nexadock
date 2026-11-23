using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
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

/// <param name="Color">The calendar's colour (stripe next to the event).</param>
/// <param name="CalendarName">Which calendar the event is from.</param>
public sealed record AgendaItem(string Time, string Title, string? Location, bool IsNow, bool AllDay, Brush Color, string? CalendarName);

public sealed record AgendaDay(string Header, IReadOnlyList<AgendaItem> Items);

/// <summary>One calendar of the Google account, with a tick to show it on the bar.</summary>
public sealed partial class CalendarChoice : ObservableObject
{
    private readonly Action<CalendarChoice> _changed;

    public CalendarChoice(GoogleCalendarInfo info, bool enabled, Action<CalendarChoice> changed)
    {
        Info = info;
        _isEnabled = enabled;
        _changed = changed;
        Color = CalendarFeed.BrushOf(info.ColorHex);
    }

    public GoogleCalendarInfo Info { get; private set; }

    public string Id => Info.Id;

    public string Name => Info.Name;

    public Brush Color { get; private set; }

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _changed(this);

    internal void Update(GoogleCalendarInfo info)
    {
        Info = info;
        Color = CalendarFeed.BrushOf(info.ColorHex);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Color));
    }
}

/// <summary>A calendar linked by its iCal address.</summary>
public sealed record LinkedFeed(string Url, string Name, Brush Color, IRelayCommand Remove);

/// <summary>
/// Events from the user's calendars, shared by the calendar widgets and Settings:
/// <list type="bullet">
/// <item>the Google account (sign-in with Google, every calendar with its own colour), and</item>
/// <item>calendars linked by an iCal address (any provider).</item>
/// </list>
/// Refreshes every ten minutes, keeps the last good result when a refresh fails, and raises a
/// Pulse shortly before a meeting.
/// </summary>
public sealed partial class CalendarFeed : ObservableObject
{
    private const string StoreKey = "calendar.feeds";
    private const string ChoicesKey = "calendar.google.choices";
    private const string ExpiredKey = "calendar.google.expired";
    private const string ExpiredPulseId = "google-signin";
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PulseLead = TimeSpan.FromMinutes(10);
    private const int AgendaDays = 7;
    private const int AgendaMaxEvents = 12;

    private readonly ICalendarSource _source;
    private readonly IGoogleCalendarService _google;
    private readonly IKeyValueStore _store;
    private readonly PulseCenter _pulse;
    private readonly IAppShell _shell;
    private readonly ILogger<CalendarFeed> _logger;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _tick;
    private readonly HashSet<string> _announced = [];
    private readonly Dictionary<string, int> _users = [];
    private readonly Dictionary<string, bool> _choices;
    private readonly Dictionary<string, string> _feedNames = new(StringComparer.Ordinal);
    private List<string> _feeds;
    private IReadOnlyList<CalendarEvent> _events = [];
    private DateTimeOffset? _updated;
    private string? _error;
    private bool _started;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _signInCts;

    public CalendarFeed(ICalendarSource source, IGoogleCalendarService google, IKeyValueStore store, PulseCenter pulse, IAppShell shell, ILogger<CalendarFeed> logger)
    {
        _source = source;
        _google = google;
        _store = store;
        _pulse = pulse;
        _shell = shell;
        _logger = logger;
        _feeds = LoadFeeds();
        _choices = LoadChoices();
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshEvery };
        _refresh.Tick += async (_, _) => await RefreshAsync();
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _tick.Tick += (_, _) => Present();
        SyncGoogleState();
        RebuildFeedList();
    }

    /// <summary>Raised after events or their presentation changed (month dots, tiles).</summary>
    public event EventHandler? Changed;

    public ObservableCollection<AgendaDay> Days { get; } = [];

    public IReadOnlySet<DateOnly> EventDays { get; private set; } = new HashSet<DateOnly>();

    /// <summary>The Google account's calendars, for Settings.</summary>
    public ObservableCollection<CalendarChoice> GoogleCalendars { get; } = [];

    /// <summary>Calendars linked by iCal address, for Settings.</summary>
    public ObservableCollection<LinkedFeed> Feeds { get; } = [];

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasNoEvents;
    [ObservableProperty] private string _inputUrl = string.Empty;
    [ObservableProperty] private string _inputMessage = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    // Google account
    [ObservableProperty] private bool _hasGoogleClient;
    [ObservableProperty] private bool _isGoogleSignedIn;
    [ObservableProperty] private bool _isSigningIn;
    [ObservableProperty] private string _googleAccount = string.Empty;
    [ObservableProperty] private string _googleMessage = string.Empty;
    [ObservableProperty] private string _clientIdInput = string.Empty;
    [ObservableProperty] private string _clientSecretInput = string.Empty;

    // The next event, for tiles.
    [ObservableProperty] private bool _hasNext;
    [ObservableProperty] private bool _isNextNow;
    [ObservableProperty] private string _nextTitle = string.Empty;
    [ObservableProperty] private string _nextWhen = string.Empty;
    [ObservableProperty] private bool _hasEventsToday;

    /// <summary>Google sign-in is possible: the OAuth client is saved but nobody is signed in.</summary>
    public bool CanSignIn => HasGoogleClient && !IsGoogleSignedIn;

    partial void OnHasGoogleClientChanged(bool value) => OnPropertyChanged(nameof(CanSignIn));

    partial void OnIsGoogleSignedInChanged(bool value) => OnPropertyChanged(nameof(CanSignIn));

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
        UpdateExpiredPulse();
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
        Present();
        if (_updated is null || DateTimeOffset.Now - _updated > TimeSpan.FromMinutes(1))
        {
            await RefreshAsync();
        }
    }

    public void OnLanguageChanged()
    {
        RebuildFeedList();
        Present();
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    /// <summary>Opens Settings on the Calendars tab.</summary>
    [RelayCommand]
    private void Manage() => _shell.ShowSettings("calendar");

    // ---- Google account ---------------------------------------------------------------------------

    [RelayCommand]
    private void SaveGoogleClient()
    {
        var id = ClientIdInput.Trim();
        var secret = ClientSecretInput.Trim();
        if (!id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase) || secret.Length < 10)
        {
            GoogleMessage = Loc.T("google.clientInvalid");
            return;
        }

        _google.SetClient(id, secret);
        ClientSecretInput = string.Empty;
        GoogleMessage = string.Empty;
        SyncGoogleState();
    }

    [RelayCommand]
    private void ChangeGoogleClient()
    {
        _google.ClearClient();
        ClientIdInput = string.Empty;
        ClientSecretInput = string.Empty;
        SyncGoogleState();
    }

    [RelayCommand]
    private async Task SignInGoogle()
    {
        if (IsSigningIn)
        {
            return;
        }

        IsSigningIn = true;
        GoogleMessage = Loc.T("google.waiting");
        _signInCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await Task.Run(() => _google.SignInAsync(OpenUrl, _signInCts.Token));
            GoogleMessage = string.Empty;
            SetExpired(false);
            SyncGoogleState();
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            GoogleMessage = Loc.T("google.timedOut");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Google sign-in failed: {Error}", ex.Message);
            GoogleMessage = ex.Message == "denied" ? Loc.T("google.denied") : Loc.F("google.failed", ex.Message);
        }
        finally
        {
            IsSigningIn = false;
            _signInCts?.Dispose();
            _signInCts = null;
        }
    }

    [RelayCommand]
    private void CancelSignIn() => _signInCts?.Cancel();

    [RelayCommand]
    private async Task SignOutGoogle()
    {
        await _google.SignOutAsync();
        SetExpired(false);
        GoogleCalendars.Clear();
        SyncGoogleState();
        await RefreshAsync();
    }

    [RelayCommand]
    private static void OpenGoogleCloud() => OpenUrl("https://console.cloud.google.com/apis/credentials");

    private void SyncGoogleState()
    {
        HasGoogleClient = _google.HasClient;
        IsGoogleSignedIn = _google.IsSignedIn;
        GoogleAccount = IsGoogleSignedIn ? Loc.F("google.signedInAs", _google.AccountEmail ?? "Google") : string.Empty;
        if (HasGoogleClient && string.IsNullOrEmpty(ClientIdInput))
        {
            ClientIdInput = _google.ClientId ?? string.Empty;
        }

        IsConnected = _feeds.Count > 0 || IsGoogleSignedIn;
    }

    /// <summary>
    /// Remembers that Google dropped the sign-in (apps in "Testing" lose it after 7 days) and asks
    /// for a new one through Pulse, also after a restart, until the user signs in or out.
    /// </summary>
    private void SetExpired(bool expired)
    {
        _store.Set(ExpiredKey, expired ? "1" : null);
        UpdateExpiredPulse();
    }

    private void UpdateExpiredPulse()
    {
        if (_store.Get(ExpiredKey) is not null && !_google.IsSignedIn && _google.HasClient)
        {
            var target = _users.ContainsKey(WidgetTypes.Agenda) ? WidgetTypes.Agenda
                : _users.ContainsKey(WidgetTypes.Calendar) ? WidgetTypes.Calendar
                : WidgetTypes.Clock;
            _pulse.Raise(new PulseAlert(ExpiredPulseId, target, PulseSeverity.Warning, "pulse.googleExpired", [], 0xE787));
        }
        else
        {
            _pulse.Clear(ExpiredPulseId);
        }
    }

    private void OnChoiceChanged(CalendarChoice choice)
    {
        _choices[choice.Id] = choice.IsEnabled;
        _store.Set(ChoicesKey, JsonSerializer.Serialize(_choices));
        _ = RefreshAsync();
    }

    private void SyncGoogleCalendars(IReadOnlyList<GoogleCalendarInfo> calendars)
    {
        foreach (var gone in GoogleCalendars.Where(c => calendars.All(i => i.Id != c.Id)).ToList())
        {
            GoogleCalendars.Remove(gone);
        }

        for (var i = 0; i < calendars.Count; i++)
        {
            var info = calendars[i];
            var existing = GoogleCalendars.FirstOrDefault(c => c.Id == info.Id);
            if (existing is null)
            {
                var enabled = _choices.TryGetValue(info.Id, out var chosen) ? chosen : info.ShownInGoogle;
                GoogleCalendars.Insert(Math.Min(i, GoogleCalendars.Count), new CalendarChoice(info, enabled, OnChoiceChanged));
            }
            else
            {
                existing.Update(info);
                var at = GoogleCalendars.IndexOf(existing);
                if (at != i)
                {
                    GoogleCalendars.Move(at, Math.Min(i, GoogleCalendars.Count - 1));
                }
            }
        }
    }

    // ---- iCal links -------------------------------------------------------------------------------

    /// <summary>Adds one or more iCal addresses (one per line, or separated by spaces).</summary>
    [RelayCommand]
    private async Task Connect()
    {
        var parts = InputUrl.Split((char[])[' ', '\t', (char)10, (char)13, ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            InputMessage = Loc.T("calendar.pasteFirst");
            return;
        }

        var added = 0;
        var problems = new List<string>();
        IsLoading = true;
        InputMessage = Loc.T("calendar.checking");
        try
        {
            foreach (var part in parts)
            {
                if (!CalendarAgenda.TryNormalizeFeedUrl(part, out var url))
                {
                    problems.Add(Loc.T("calendar.invalidUrl"));
                    continue;
                }

                if (_feeds.Contains(url, StringComparer.Ordinal))
                {
                    continue;
                }

                // Try it first, so a wrong link is reported here rather than silently saved.
                var (from, to) = Window();
                var result = await _source.FetchAsync([url], from, to, CancellationToken.None);
                if (result.FailedFeeds > 0)
                {
                    problems.Add(Loc.F("calendar.connectFailed", result.Error ?? "?"));
                    continue;
                }

                if (result.FeedNames?.TryGetValue(url, out var name) == true)
                {
                    _feedNames[url] = name;
                }

                _feeds.Add(url);
                added++;
            }
        }
        finally
        {
            IsLoading = false;
        }

        if (added > 0)
        {
            SaveFeeds();
            _logger.LogInformation("{Added} calendar link(s) added; {Count} linked.", added, _feeds.Count);
            InputUrl = string.Empty;
            RebuildFeedList();
            SyncGoogleState();
            await RefreshAsync();
        }

        InputMessage = problems.Count > 0
            ? string.Join(" ", problems.Distinct())
            : added > 0 ? Loc.F("calendar.added", added) : Loc.T("calendar.alreadyAdded");
    }

    private void RemoveFeed(string url)
    {
        _feeds.Remove(url);
        _feedNames.Remove(url);
        SaveFeeds();
        RebuildFeedList();
        SyncGoogleState();
        _ = RefreshAsync();
    }

    private void RebuildFeedList()
    {
        Feeds.Clear();
        foreach (var url in _feeds)
        {
            var name = _feedNames.TryGetValue(url, out var n) ? n : CalendarAgenda.DescribeFeed(url);
            var color = _events.FirstOrDefault(e => e.CalendarName == name)?.ColorHex;
            var u = url;
            Feeds.Add(new LinkedFeed(url, name, BrushOf(color), new RelayCommand(() => RemoveFeed(u))));
        }
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

    internal static Brush BrushOf(string? hex)
    {
        try
        {
            if (!string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch (FormatException)
        {
        }

        return Brushes.SlateGray;
    }

    // ---- Loading ------------------------------------------------------------------------------------

    private async Task RefreshAsync()
    {
        if (_feeds.Count == 0 && !_google.IsSignedIn)
        {
            _events = [];
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
            var errors = new List<string>();
            var events = new List<CalendarEvent>();
            var anyLoaded = false;

            if (feeds.Count > 0)
            {
                var result = await Task.Run(() => _source.FetchAsync(feeds, from, to, cts.Token), cts.Token);
                if (result.FailedFeeds < feeds.Count)
                {
                    anyLoaded = true;
                    events.AddRange(result.Events);
                }

                foreach (var (url, name) in result.FeedNames ?? new Dictionary<string, string>())
                {
                    _feedNames[url] = name;
                }

                if (result.FailedFeeds > 0)
                {
                    errors.Add(result.Error ?? "?");
                }
            }

            if (_google.IsSignedIn)
            {
                try
                {
                    var calendars = await Task.Run(() => _google.GetCalendarsAsync(cts.Token), cts.Token);
                    SyncGoogleCalendars(calendars);
                    var wanted = GoogleCalendars.Where(c => c.IsEnabled).Select(c => c.Info).ToList();
                    var lists = await Task.WhenAll(wanted.Select(c => Task.Run(() => _google.GetEventsAsync(c, from, to, cts.Token), cts.Token)));
                    events.AddRange(lists.SelectMany(l => l));
                    anyLoaded = true;
                }
                catch (GoogleSignInExpiredException)
                {
                    _logger.LogWarning("Google sign-in expired or was revoked.");
                    GoogleMessage = Loc.T("google.expired");
                    SetExpired(true);
                    GoogleCalendars.Clear();
                    SyncGoogleState();
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    errors.Add("Google: " + ex.Message);
                }
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (anyLoaded || (feeds.Count == 0 && !_google.IsSignedIn))
            {
                _events = events
                    .GroupBy(e => e.Key)
                    .Select(g => g.First())
                    .OrderBy(e => e.Start)
                    .ToList();
                _updated = DateTimeOffset.Now;
            }

            _error = errors.Count > 0 ? string.Join("; ", errors) : null;
            RebuildFeedList();
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
                e.AllDay,
                BrushOf(e.ColorHex),
                e.CalendarName)).ToList();
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

    private Dictionary<string, bool> LoadChoices()
    {
        try
        {
            var json = _store.Get(ChoicesKey);
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
