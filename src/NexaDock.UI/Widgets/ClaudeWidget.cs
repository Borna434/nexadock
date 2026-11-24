using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Models;
using NexaDock.Core.Pulse;
using NexaDock.Core.Usage;
using NexaDock.Core.Widgets;
using NexaDock.UI.Localization;
using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

public sealed record UsageBar(string Label, double HeightPercent, string ToolTip, bool IsToday);

public sealed record ModelRow(string Name, string Detail);

/// <summary>
/// Claude Code usage from local logs: the current 5-hour session window (and when it resets),
/// today, the last seven days and models. Plan limits are not public, so the percentage compares
/// the session with the user's own busiest session of the last 30 days.
/// </summary>
public sealed partial class ClaudeWidget : WidgetViewModel
{
    /// <summary>Do not raise a Pulse for tiny "records" (a quiet first week).</summary>
    public const long MinimumRecordForPulse = 200_000;

    private readonly IClaudeUsageSource _source;
    private readonly PulseCenter _pulse;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _clock;
    private ClaudeUsageSummary _summary = ClaudeUsageSummary.Empty(false);
    private bool _loading;

    public ClaudeWidget(WidgetPlacement placement, IClaudeUsageSource source, PulseCenter pulse)
        : base(placement)
    {
        _source = source;
        _pulse = pulse;
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _refresh.Tick += async (_, _) => { if (!QuietMode.IsActive) await LoadAsync(); };
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _clock.Tick += (_, _) => { if (!QuietMode.IsActive) Present(); };
    }

    public override bool HasCard => true;

    public ObservableCollection<UsageBar> Days { get; } = [];

    public ObservableCollection<ModelRow> Models { get; } = [];

    [ObservableProperty] private string _headline = "Claude";
    [ObservableProperty] private string _subline = string.Empty;
    [ObservableProperty] private double _windowProgress;
    [ObservableProperty] private double _recordShare;
    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private bool _hasRecord;
    [ObservableProperty] private string _sessionTokens = "–";
    [ObservableProperty] private string _sessionMessages = string.Empty;
    [ObservableProperty] private string _sessionTimes = string.Empty;
    [ObservableProperty] private string _sessionCache = string.Empty;
    [ObservableProperty] private string _recordText = string.Empty;
    [ObservableProperty] private string _todayText = string.Empty;
    [ObservableProperty] private string _weekText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    public override async void Start()
    {
        Present();
        _refresh.Start();
        _clock.Start();
        await LoadAsync();
    }

    public override async void OnCardOpened() => await LoadAsync();

    public override void OnLanguageChanged() => Present();

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            var found = _source.LogsFound;
            var entries = found ? await Task.Run(_source.ReadRecent) : [];
            _summary = ClaudeUsageCalculator.Summarize(entries, DateTimeOffset.UtcNow, TimeZoneInfo.Local, found);
            Present();
            UpdatePulse();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Present()
    {
        var culture = Loc.Instance.Culture;
        var s = _summary;
        var now = DateTimeOffset.UtcNow;

        if (!s.LogsFound)
        {
            Headline = "Claude";
            Subline = Loc.T("claude.noLogs");
            StatusText = Loc.T("claude.noLogsDetail");
            HasSession = false;
            return;
        }

        StatusText = string.Empty;
        var block = s.ActiveBlock is { } b && b.End > now ? b : null;
        HasSession = block is not null;

        if (block is not null)
        {
            var left = block.End - now;
            WindowProgress = Math.Clamp((now - block.Start) / (block.End - block.Start) * 100, 0, 100);
            Headline = ClaudeUsageCalculator.Compact(block.Tokens, culture);
            Subline = Loc.F("claude.resetsIn", Duration(left));
            SessionTokens = ClaudeUsageCalculator.Compact(block.Tokens, culture);
            SessionMessages = Loc.F("claude.messages", block.Messages);
            SessionTimes = Loc.F("claude.window",
                block.Start.ToLocalTime().ToString("t", culture),
                block.End.ToLocalTime().ToString("t", culture),
                Duration(left));
            SessionCache = Loc.F("claude.cacheReads", ClaudeUsageCalculator.Compact(block.CacheReadTokens, culture));
        }
        else
        {
            WindowProgress = 0;
            Headline = "Claude";
            Subline = s.TodayTokens > 0
                ? Loc.F("claude.todayShort", ClaudeUsageCalculator.Compact(s.TodayTokens, culture))
                : Loc.T("claude.noSession");
        }

        HasRecord = s.ActiveShareOfRecord is not null;
        RecordShare = s.ActiveShareOfRecord ?? 0;
        RecordText = HasRecord ? Loc.F("claude.ofRecord", RecordShare, ClaudeUsageCalculator.Compact(s.LargestBlockTokens, culture)) : string.Empty;
        TodayText = Loc.F("claude.today", ClaudeUsageCalculator.Compact(s.TodayTokens, culture), s.TodayMessages);
        WeekText = Loc.F("claude.week", ClaudeUsageCalculator.Compact(s.WeekTokens, culture));

        var max = Math.Max(1, s.LastSevenDays.Select(d => d.Tokens).DefaultIfEmpty(0).Max());
        var today = DateOnly.FromDateTime(DateTime.Now);
        Days.Clear();
        foreach (var d in s.LastSevenDays)
        {
            Days.Add(new UsageBar(
                d.Day.ToDateTime(TimeOnly.MinValue).ToString("ddd", culture),
                Math.Max(d.Tokens > 0 ? 4 : 0, 100.0 * d.Tokens / max),
                $"{d.Day.ToDateTime(TimeOnly.MinValue).ToString("d", culture)}: {ClaudeUsageCalculator.Compact(d.Tokens, culture)} · {Loc.F("claude.messages", d.Messages)}",
                d.Day == today));
        }

        Models.Clear();
        foreach (var m in s.Models.Take(4))
        {
            Models.Add(new ModelRow(m.Model, $"{ClaudeUsageCalculator.Compact(m.Tokens, culture)} · {Loc.F("claude.messages", m.Messages)}"));
        }
    }

    private void UpdatePulse()
    {
        var s = _summary;
        if (s.ActiveBlock is { } b && s.ActiveShareOfRecord is >= 90 && s.LargestBlockTokens >= MinimumRecordForPulse)
        {
            _pulse.Raise(new PulseAlert("claude", WidgetTypes.Claude, PulseSeverity.Info, "pulse.claude",
                [s.ActiveShareOfRecord.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture), Duration(b.End - DateTimeOffset.UtcNow)],
                0xE945));
        }
        else
        {
            _pulse.Clear("claude");
        }
    }

    private static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return t.TotalHours >= 1 ? Loc.F("common.hoursMinutes", (int)t.TotalHours, t.Minutes) : Loc.F("common.minutes", Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)));
    }

    public override void Dispose()
    {
        _refresh.Stop();
        _clock.Stop();
        base.Dispose();
    }
}
