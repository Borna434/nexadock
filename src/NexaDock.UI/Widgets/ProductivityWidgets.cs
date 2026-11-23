using System.Collections.ObjectModel;
using System.Media;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Productivity;
using NexaDock.Core.Pulse;
using NexaDock.Core.Widgets;
using NexaDock.UI.Localization;
using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

internal static class TimeText
{
    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    public static string Precise(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}" : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
}

/// <summary>Pomodoro: focus rounds and breaks, with a Pulse and a sound when a phase ends.</summary>
public sealed partial class FocusWidget : WidgetViewModel
{
    private readonly FocusSession _session = new(TimeProvider.System);
    private readonly PulseCenter _pulse;
    private readonly IAppShell _shell;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _timer;

    public FocusWidget(WidgetPlacement placement, PulseCenter pulse, IAppShell shell, ISettingsService settings)
        : base(placement)
    {
        _pulse = pulse;
        _shell = shell;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
    }

    public override bool HasCard => true;

    public string PlayGlyph { get; } = WidgetCatalog.Glyph(0xE768);

    public string PauseGlyph { get; } = WidgetCatalog.Glyph(0xE769);

    [ObservableProperty] private string _remainingText = "25:00";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _phaseText = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBreak;
    [ObservableProperty] private string _roundsText = string.Empty;
    [ObservableProperty] private int _focusMinutes = 25;
    [ObservableProperty] private int _breakMinutes = 5;

    public string ToggleGlyph => IsRunning ? PauseGlyph : PlayGlyph;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ToggleGlyph));

    public override void ApplySettings(DockSettings s)
    {
        _session.FocusLength = TimeSpan.FromMinutes(s.FocusMinutes);
        _session.ShortBreakLength = TimeSpan.FromMinutes(s.ShortBreakMinutes);
        _session.LongBreakLength = TimeSpan.FromMinutes(s.LongBreakMinutes);
        FocusMinutes = s.FocusMinutes;
        BreakMinutes = s.ShortBreakMinutes;
        Refresh();
    }

    public override void Start() => _timer.Start();

    private void Tick()
    {
        var finished = _session.Tick();
        if (finished is { } phase)
        {
            var key = phase == FocusPhase.Focus ? "focus.doneFocus" : "focus.doneBreak";
            _pulse.Raise(new PulseAlert("focus", WidgetTypes.Focus, PulseSeverity.Info, key, [], 0xE7C1));
            _shell.Notify(Loc.T("widget.focus.name"), Loc.T(key));
            SystemSounds.Asterisk.Play();
        }

        Refresh();
    }

    private void Refresh()
    {
        RemainingText = TimeText.Clock(_session.Remaining);
        ProgressPercent = _session.Phase == FocusPhase.Idle ? 0 : _session.Progress * 100;
        IsRunning = _session.IsRunning;
        IsBreak = _session.Phase is FocusPhase.ShortBreak or FocusPhase.LongBreak;
        PhaseText = Loc.T(_session.Phase switch
        {
            FocusPhase.Focus => "focus.phase.focus",
            FocusPhase.ShortBreak => "focus.phase.short",
            FocusPhase.LongBreak => "focus.phase.long",
            _ => "focus.phase.idle",
        });
        RoundsText = Loc.F("focus.rounds", _session.CompletedRounds);
    }

    [RelayCommand]
    private void Toggle()
    {
        if (_session.IsRunning)
        {
            _session.Pause();
        }
        else
        {
            _session.Start();
            _pulse.Clear("focus");
        }

        Refresh();
    }

    [RelayCommand]
    private void Skip()
    {
        _session.Skip();
        Refresh();
    }

    [RelayCommand]
    private void Reset()
    {
        _session.Reset();
        _pulse.Clear("focus");
        Refresh();
    }

    [RelayCommand]
    private void ChangeFocus(string delta) =>
        _settings.Update(s => s with { FocusMinutes = s.FocusMinutes + int.Parse(delta, System.Globalization.CultureInfo.InvariantCulture) });

    [RelayCommand]
    private void ChangeBreak(string delta) =>
        _settings.Update(s => s with { ShortBreakMinutes = s.ShortBreakMinutes + int.Parse(delta, System.Globalization.CultureInfo.InvariantCulture) });

    public override void OnCardOpened() => _pulse.Clear("focus");

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

/// <summary>One-click countdown with presets.</summary>
public sealed partial class TimerWidget : WidgetViewModel
{
    private readonly Countdown _countdown = new(TimeProvider.System);
    private readonly PulseCenter _pulse;
    private readonly IAppShell _shell;
    private readonly DispatcherTimer _timer;

    public TimerWidget(WidgetPlacement placement, PulseCenter pulse, IAppShell shell)
        : base(placement)
    {
        _pulse = pulse;
        _shell = shell;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        Refresh();
    }

    public override bool HasCard => true;

    public IReadOnlyList<int> Presets { get; } = [1, 3, 5, 10, 15, 25, 45, 60];

    public string PlayGlyph { get; } = WidgetCatalog.Glyph(0xE768);

    public string PauseGlyph { get; } = WidgetCatalog.Glyph(0xE769);

    [ObservableProperty] private string _remainingText = "05:00";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isFinished;

    public string ToggleGlyph => IsRunning ? PauseGlyph : PlayGlyph;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ToggleGlyph));

    public override void Start() => _timer.Start();

    private void Tick()
    {
        if (_countdown.Tick())
        {
            _pulse.Raise(new PulseAlert("timer", WidgetTypes.Timer, PulseSeverity.Info, "timer.done", [], 0xE916));
            _shell.Notify(Loc.T("widget.timer.name"), Loc.T("timer.done"));
            SystemSounds.Exclamation.Play();
        }

        Refresh();
    }

    private void Refresh()
    {
        RemainingText = TimeText.Clock(_countdown.Remaining);
        ProgressPercent = _countdown.Progress * 100;
        IsRunning = _countdown.IsRunning;
        IsFinished = _countdown.IsFinished;
    }

    [RelayCommand]
    private void StartPreset(int minutes)
    {
        _countdown.Set(TimeSpan.FromMinutes(minutes));
        _countdown.Start();
        _pulse.Clear("timer");
        Refresh();
    }

    [RelayCommand]
    private void Add(string minutes)
    {
        var add = TimeSpan.FromMinutes(int.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture));
        var running = _countdown.IsRunning;
        _countdown.Set(_countdown.Remaining + add);
        if (running)
        {
            _countdown.Start();
        }

        Refresh();
    }

    [RelayCommand]
    private void Toggle()
    {
        if (_countdown.IsRunning)
        {
            _countdown.Pause();
        }
        else
        {
            _countdown.Start();
            _pulse.Clear("timer");
        }

        Refresh();
    }

    [RelayCommand]
    private void Reset()
    {
        _countdown.Reset();
        _pulse.Clear("timer");
        Refresh();
    }

    public override void OnCardOpened() => _pulse.Clear("timer");

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed partial class StopwatchWidget : WidgetViewModel
{
    private readonly LapStopwatch _watch = new(TimeProvider.System);
    private readonly DispatcherTimer _timer;

    public StopwatchWidget(WidgetPlacement placement)
        : base(placement)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Refresh();
    }

    public override bool HasCard => true;

    public string PlayGlyph { get; } = WidgetCatalog.Glyph(0xE768);

    public string PauseGlyph { get; } = WidgetCatalog.Glyph(0xE769);

    public ObservableCollection<LapRow> Laps { get; } = [];

    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _preciseText = "00:00.00";
    [ObservableProperty] private bool _isRunning;

    public string ToggleGlyph => IsRunning ? PauseGlyph : PlayGlyph;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ToggleGlyph));

    private void Refresh()
    {
        var e = _watch.Elapsed;
        ElapsedText = TimeText.Clock(e);
        PreciseText = TimeText.Precise(e);
        IsRunning = _watch.IsRunning;
        if (!IsRunning)
        {
            _timer.Stop();
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (_watch.IsRunning)
        {
            _watch.Pause();
        }
        else
        {
            _watch.Start();
            _timer.Start();
        }

        Refresh();
    }

    [RelayCommand]
    private void Lap()
    {
        if (!_watch.IsRunning)
        {
            return;
        }

        _watch.Lap();
        var laps = _watch.Laps;
        Laps.Insert(0, new LapRow(Loc.F("stopwatch.lap", laps.Count), TimeText.Precise(laps[^1])));
    }

    [RelayCommand]
    private void Reset()
    {
        _watch.Reset();
        Laps.Clear();
        Refresh();
    }

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed record LapRow(string Label, string Time);

public sealed partial class TodoRowViewModel(TodoItem item, Action<TodoRowViewModel> changed, Action<TodoRowViewModel> delete) : ObservableObject
{
    public string Id { get; } = item.Id;

    public string Text { get; } = item.Text;

    public DateTimeOffset Created { get; } = item.Created;

    [ObservableProperty] private bool _done = item.Done;

    partial void OnDoneChanged(bool value) => changed(this);

    [RelayCommand]
    private void Delete() => delete(this);

    public TodoItem ToItem() => new(Id, Text, Done, Created);
}

/// <summary>To-do list stored locally; the tile shows the next open task and a progress ring.</summary>
public sealed partial class TodosWidget : WidgetViewModel
{
    private const string StoreKey = "todos";
    private readonly IKeyValueStore _store;

    public TodosWidget(WidgetPlacement placement, IKeyValueStore store)
        : base(placement)
    {
        _store = store;
        Load();
    }

    public override bool HasCard => true;

    public override bool WantsKeyboard => true;

    public ObservableCollection<TodoRowViewModel> Items { get; } = [];

    [ObservableProperty] private string _newText = string.Empty;
    [ObservableProperty] private string _nextText = string.Empty;
    [ObservableProperty] private double _donePercent;
    [ObservableProperty] private string _countText = string.Empty;

    [RelayCommand]
    private void Add()
    {
        var text = NewText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Items.Add(Row(new TodoItem(Guid.NewGuid().ToString("N"), text.Length > 200 ? text[..200] : text, false, DateTimeOffset.Now)));
        NewText = string.Empty;
        Save();
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        foreach (var done in Items.Where(i => i.Done).ToList())
        {
            Items.Remove(done);
        }

        Save();
    }

    private TodoRowViewModel Row(TodoItem item) => new(item, _ => Save(), r =>
    {
        Items.Remove(r);
        Save();
    });

    private void Load()
    {
        try
        {
            var json = _store.Get(StoreKey);
            var items = string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<TodoItem>>(json) ?? [];
            foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.Text)))
            {
                Items.Add(Row(item));
            }
        }
        catch (JsonException)
        {
            // Unreadable list: start empty rather than crash; the bad value is overwritten on next save.
        }

        Summarize();
    }

    private void Save()
    {
        _store.Set(StoreKey, JsonSerializer.Serialize(Items.Select(i => i.ToItem()).ToList()));
        Summarize();
    }

    private void Summarize()
    {
        var total = Items.Count;
        var done = Items.Count(i => i.Done);
        DonePercent = total == 0 ? 0 : 100.0 * done / total;
        NextText = Items.FirstOrDefault(i => !i.Done)?.Text ?? (total == 0 ? Loc.T("todos.empty") : Loc.T("todos.allDone"));
        CountText = total == 0 ? string.Empty : $"{done}/{total}";
    }

    public override void ApplySettings(DockSettings settings) => Summarize();
}

/// <summary>A quick scratch note, saved locally as you type.</summary>
public sealed partial class NotesWidget : WidgetViewModel
{
    private const string StoreKey = "notes";
    private readonly IKeyValueStore _store;
    private readonly DispatcherTimer _save;

    public NotesWidget(WidgetPlacement placement, IKeyValueStore store)
        : base(placement)
    {
        _store = store;
        _text = _store.Get(StoreKey) ?? string.Empty;
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            _store.Set(StoreKey, Text);
        };
        UpdatePreview();
    }

    public override bool HasCard => true;

    public override bool WantsKeyboard => true;

    [ObservableProperty] private string _text;
    [ObservableProperty] private string _preview = string.Empty;

    partial void OnTextChanged(string value)
    {
        UpdatePreview();
        _save.Stop();
        _save.Start();
    }

    private void UpdatePreview()
    {
        var first = Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        Preview = first ?? Loc.T("notes.empty");
    }

    public override void ApplySettings(DockSettings settings) => UpdatePreview();

    public override void OnCardClosed()
    {
        _save.Stop();
        _store.Set(StoreKey, Text);
    }

    public override void Dispose()
    {
        OnCardClosed();
        base.Dispose();
    }
}
