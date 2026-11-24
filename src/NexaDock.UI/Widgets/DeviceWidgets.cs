using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Pulse;
using NexaDock.Core.Widgets;
using NexaDock.UI.Localization;
using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

/// <summary>Speaker volume: scroll on the tile to adjust, card with slider, mute and microphone.</summary>
public sealed partial class VolumeWidget : WidgetViewModel
{
    private readonly IAudioService _audio;
    private readonly DispatcherTimer _timer;
    private bool _syncing;

    public VolumeWidget(WidgetPlacement placement, IAudioService audio)
        : base(placement)
    {
        _audio = audio;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Refresh(); };
    }

    public override bool HasCard => true;

    public override bool HandlesWheel => true;

    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private string _glyph = WidgetCatalog.Glyph(0xE767);
    [ObservableProperty] private string _percentText = string.Empty;
    [ObservableProperty] private string _deviceName = string.Empty;
    [ObservableProperty] private bool _available;
    [ObservableProperty] private bool _micAvailable;
    [ObservableProperty] private bool _micMuted;

    public override void Start()
    {
        Refresh();
        _timer.Start();
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_syncing)
        {
            _audio.SetSpeakerVolume(value / 100.0);
            UpdateText(value, Muted);
        }
    }

    public override void OnWheel(int delta)
    {
        var step = delta > 0 ? 2 : -2;
        Volume = Math.Clamp(Math.Round(Volume) + step, 0, 100);
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _audio.SetSpeakerMuted(!Muted);
        Refresh();
    }

    [RelayCommand]
    private void ToggleMic()
    {
        _audio.SetMicrophoneMuted(!MicMuted);
        Refresh();
    }

    private void Refresh()
    {
        var s = _audio.GetSpeaker();
        var m = _audio.GetMicrophone();
        _syncing = true;
        try
        {
            Available = s.Available;
            Volume = Math.Round(s.Volume * 100);
            Muted = s.Muted;
            DeviceName = s.Available ? s.DeviceName : Loc.T("volume.noDevice");
            MicAvailable = m.Available;
            MicMuted = m.Muted;
            UpdateText(Volume, Muted);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateText(double volume, bool muted)
    {
        PercentText = muted ? Loc.T("volume.muted") : $"{volume:0}%";
        Glyph = WidgetCatalog.Glyph(muted || volume < 1 ? 0xE74F : volume < 34 ? 0xE993 : volume < 67 ? 0xE994 : 0xE995);
    }

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

/// <summary>One-click microphone mute for every app at once.</summary>
public sealed partial class MicrophoneWidget : WidgetViewModel
{
    private readonly IAudioService _audio;
    private readonly DispatcherTimer _timer;

    public MicrophoneWidget(WidgetPlacement placement, IAudioService audio)
        : base(placement)
    {
        _audio = audio;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Refresh(); };
    }

    [ObservableProperty] private bool _muted;
    [ObservableProperty] private bool _available;
    [ObservableProperty] private string _glyph = WidgetCatalog.Glyph(0xE720);
    [ObservableProperty] private string _stateText = string.Empty;

    public override void Start()
    {
        Refresh();
        _timer.Start();
    }

    public override void OnClick()
    {
        if (Available)
        {
            _audio.SetMicrophoneMuted(!Muted);
            Refresh();
        }
    }

    public override void ApplySettings(DockSettings settings) => Refresh();

    private void Refresh()
    {
        var m = _audio.GetMicrophone();
        Available = m.Available;
        Muted = m.Muted;
        Glyph = WidgetCatalog.Glyph(m.Muted || !m.Available ? 0xEC54 : 0xE720);
        StateText = !m.Available ? Loc.T("mic.none") : m.Muted ? Loc.T("mic.muted") : Loc.T("mic.live");
    }

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

/// <summary>Download/upload speed with a 60-second chart.</summary>
public sealed partial class NetworkWidget : WidgetViewModel
{
    private const int History = 60;
    private readonly IStorageNetworkMonitor _monitor;
    private readonly DispatcherTimer _timer;
    private readonly List<double> _down = [];
    private readonly List<double> _up = [];
    private NetworkTotals? _last;
    private DateTime _lastAt;

    public NetworkWidget(WidgetPlacement placement, IStorageNetworkMonitor monitor)
        : base(placement)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Sample(); };
    }

    public override bool HasCard => true;

    [ObservableProperty] private string _downText = "–";
    [ObservableProperty] private string _upText = "–";
    [ObservableProperty] private string _adapters = string.Empty;
    [ObservableProperty] private PointCollection _downPoints = [];
    [ObservableProperty] private PointCollection _upPoints = [];
    [ObservableProperty] private string _peakText = string.Empty;

    public override void Start()
    {
        Sample();
        _timer.Start();
    }

    private void Sample()
    {
        var now = DateTime.UtcNow;
        var totals = _monitor.GetNetworkTotals();
        if (_last is not null)
        {
            var seconds = Math.Max(0.2, (now - _lastAt).TotalSeconds);
            Push(_down, Math.Max(0, (totals.BytesReceived - _last.BytesReceived) / seconds));
            Push(_up, Math.Max(0, (totals.BytesSent - _last.BytesSent) / seconds));
            DownText = Rate(_down[^1]);
            UpText = Rate(_up[^1]);
            UpdateChart();
        }

        Adapters = totals.AdapterNames;
        _last = totals;
        _lastAt = now;
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > History)
        {
            list.RemoveAt(0);
        }
    }

    private void UpdateChart()
    {
        var peak = Math.Max(1024, Math.Max(_down.DefaultIfEmpty(0).Max(), _up.DefaultIfEmpty(0).Max()));
        DownPoints = Points(_down, peak);
        UpPoints = Points(_up, peak);
        PeakText = Loc.F("network.peak", Rate(peak));
    }

    /// <summary>Maps samples into a 260×70 chart area (right-aligned, newest at the right edge).</summary>
    private static PointCollection Points(List<double> values, double peak)
    {
        const double w = 260, h = 70;
        var points = new PointCollection { new(w - (values.Count - 1) * (w / (History - 1)), h) };
        for (var i = 0; i < values.Count; i++)
        {
            var x = w - (values.Count - 1 - i) * (w / (History - 1));
            points.Add(new System.Windows.Point(x, h - values[i] / peak * (h - 4)));
        }

        points.Add(new System.Windows.Point(w, h));
        points.Freeze();
        return points;
    }

    public static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / (1024 * 1024):0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s",
    };

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed record DriveRow(string Title, string Detail, double UsedPercent, bool IsLow);

public sealed partial class StorageWidget : WidgetViewModel
{
    private readonly IStorageNetworkMonitor _monitor;
    private readonly DispatcherTimer _timer;

    public StorageWidget(WidgetPlacement placement, IStorageNetworkMonitor monitor)
        : base(placement)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Refresh(); };
    }

    public override bool HasCard => true;

    public ObservableCollection<DriveRow> Drives { get; } = [];

    [ObservableProperty] private string _freeText = "–";
    [ObservableProperty] private double _usedPercent;
    [ObservableProperty] private string _driveName = string.Empty;

    public override void Start()
    {
        Refresh();
        _timer.Start();
    }

    public override void ApplySettings(DockSettings settings) => Refresh();

    public override void OnCardOpened() => Refresh();

    private void Refresh()
    {
        var drives = _monitor.GetDrives();
        Drives.Clear();
        foreach (var d in drives)
        {
            var used = d.TotalBytes == 0 ? 0 : 100.0 * (d.TotalBytes - d.FreeBytes) / d.TotalBytes;
            Drives.Add(new DriveRow($"{d.Label} ({d.Name})", Loc.F("storage.freeOf", Gb(d.FreeBytes), Gb(d.TotalBytes)), used, used >= 90));
        }

        var system = drives.FirstOrDefault(d => d.IsSystem) ?? drives.FirstOrDefault();
        if (system is not null)
        {
            DriveName = system.Name;
            UsedPercent = system.TotalBytes == 0 ? 0 : 100.0 * (system.TotalBytes - system.FreeBytes) / system.TotalBytes;
            FreeText = Loc.F("storage.free", Gb(system.FreeBytes));
        }
    }

    private static string Gb(ulong bytes) => bytes >= 1UL << 40 ? $"{bytes / (double)(1UL << 40):0.0} TB" : $"{bytes / 1073741824.0:0} GB";

    [RelayCommand]
    private void OpenThisPc() => _monitor.OpenThisPc();

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed partial class RecycleBinWidget : WidgetViewModel
{
    private readonly IStorageNetworkMonitor _monitor;
    private readonly DispatcherTimer _timer;

    public RecycleBinWidget(WidgetPlacement placement, IStorageNetworkMonitor monitor)
        : base(placement)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Refresh(); };
    }

    public override bool HasCard => true;

    public string Glyph { get; } = WidgetCatalog.Glyph(0xE74D);

    [ObservableProperty] private string _countText = "0";
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;

    public override void Start()
    {
        Refresh();
        _timer.Start();
    }

    public override void ApplySettings(DockSettings settings) => Refresh();

    public override void OnCardOpened() => Refresh();

    private void Refresh()
    {
        var bin = _monitor.GetRecycleBin();
        IsEmpty = bin.Items == 0;
        CountText = bin.Items.ToString(Loc.Instance.Culture);
        DetailText = IsEmpty ? Loc.T("bin.empty") : Loc.F("bin.items", bin.Items, Services.Format.Bytes(bin.Bytes));
    }

    [RelayCommand]
    private void Open() => _monitor.OpenRecycleBin();

    [RelayCommand]
    private void Empty()
    {
        _monitor.EmptyRecycleBin();
        Refresh();
    }

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed partial class KeyboardWidget : WidgetViewModel
{
    private readonly IKeyboardLayoutService _layouts;
    private readonly IWindowTracker _tracker;

    public KeyboardWidget(WidgetPlacement placement, IKeyboardLayoutService layouts, IWindowTracker tracker)
        : base(placement)
    {
        _layouts = layouts;
        _tracker = tracker;
    }

    [ObservableProperty] private string _short = "??";
    [ObservableProperty] private string _layoutName = string.Empty;

    public override void Start()
    {
        _tracker.Changed += OnChanged;
        Refresh();

        // Layout changes inside the same window raise no window event; poll gently.
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { if (!QuietMode.IsActive) Refresh(); };
        timer.Start();
        _poll = timer;
    }

    private DispatcherTimer? _poll;

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    public override void OnClick()
    {
        _layouts.SwitchNext();
        Dispatcher.CurrentDispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        var info = _layouts.GetCurrent();
        Short = info.Short;
        LayoutName = info.Name;
    }

    public override void Dispose()
    {
        _tracker.Changed -= OnChanged;
        _poll?.Stop();
        base.Dispose();
    }
}

public sealed partial class RadioToggleViewModel(RadioKind kind, string glyph, Func<RadioToggleViewModel, Task> toggle) : ObservableObject
{
    public RadioKind Kind { get; } = kind;

    public string Glyph { get; } = glyph;

    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _label = string.Empty;

    [RelayCommand]
    private Task Toggle() => toggle(this);
}

/// <summary>Wi-Fi and Bluetooth switches.</summary>
public sealed partial class QuickTogglesWidget : WidgetViewModel
{
    private readonly IRadioService _radios;
    private readonly IShellCommands _shell;
    private readonly DispatcherTimer _timer;

    public QuickTogglesWidget(WidgetPlacement placement, IRadioService radios, IShellCommands shell)
        : base(placement)
    {
        _radios = radios;
        _shell = shell;
        WiFi = new RadioToggleViewModel(RadioKind.WiFi, WidgetCatalog.Glyph(0xE701), ToggleAsync);
        Bluetooth = new RadioToggleViewModel(RadioKind.Bluetooth, WidgetCatalog.Glyph(0xE702), ToggleAsync);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => { if (!QuietMode.IsActive) await RefreshAsync(); };
    }

    public RadioToggleViewModel WiFi { get; }

    public RadioToggleViewModel Bluetooth { get; }

    [ObservableProperty] private bool _hasAny;

    public string MoreGlyph { get; } = WidgetCatalog.Glyph(0xE712);

    public override async void Start()
    {
        await RefreshAsync();
        _timer.Start();
    }

    public override async void ApplySettings(DockSettings settings) => await RefreshAsync();

    [RelayCommand]
    private void OpenQuickSettings() => _shell.OpenQuickSettings();

    private async Task ToggleAsync(RadioToggleViewModel toggle)
    {
        await _radios.SetAsync(toggle.Kind, !toggle.IsOn);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var radios = await _radios.GetRadiosAsync();
        foreach (var t in new[] { WiFi, Bluetooth })
        {
            var r = radios.FirstOrDefault(x => x.Kind == t.Kind);
            t.IsAvailable = r is not null;
            t.IsOn = r?.IsOn ?? false;
            var name = Loc.T(t.Kind == RadioKind.WiFi ? "toggles.wifi" : "toggles.bluetooth");
            t.Label = $"{name}: {Loc.T(t.IsOn ? "common.on" : "common.off")}";
        }

        HasAny = WiFi.IsAvailable || Bluetooth.IsAvailable;
    }

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed partial class BrightnessRowViewModel(DisplayBrightness display, Action<string, int> set) : ObservableObject
{
    public string Id { get; } = display.Id;

    public string Name { get; } = display.Name;

    [ObservableProperty] private double _percent = display.Percent;

    partial void OnPercentChanged(double value) => set(Id, (int)Math.Round(value));
}

/// <summary>Brightness for laptop panels and DDC/CI monitors. Hardware calls run in the background.</summary>
public sealed partial class BrightnessWidget : WidgetViewModel
{
    private readonly IBrightnessService _brightness;
    private readonly DispatcherTimer _apply;
    private (string Id, int Percent)? _pending;

    public BrightnessWidget(WidgetPlacement placement, IBrightnessService brightness)
        : base(placement)
    {
        _brightness = brightness;
        _apply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _apply.Tick += (_, _) => Flush();
    }

    public override bool HasCard => true;

    public override bool HandlesWheel => true;

    public ObservableCollection<BrightnessRowViewModel> Displays { get; } = [];

    [ObservableProperty] private string _percentText = "–";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isSupported = true;

    public override async void Start() => await LoadAsync();

    public override async void OnCardOpened() => await LoadAsync();

    public override void OnWheel(int delta)
    {
        if (Displays.Count == 0)
        {
            return;
        }

        foreach (var d in Displays)
        {
            d.Percent = Math.Clamp(Math.Round(d.Percent) + (delta > 0 ? 5 : -5), 0, 100);
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var displays = await Task.Run(_brightness.GetDisplays);
        Displays.Clear();
        foreach (var d in displays)
        {
            Displays.Add(new BrightnessRowViewModel(d, Queue));
        }

        IsSupported = Displays.Count > 0;
        IsLoading = false;
        UpdateText();
    }

    private void Queue(string id, int percent)
    {
        _pending = (id, percent);
        UpdateText();
        _apply.Stop();
        _apply.Start();
    }

    private void Flush()
    {
        _apply.Stop();
        if (_pending is not { } p)
        {
            return;
        }

        _pending = null;
        var targets = Displays.Select(d => (d.Id, (int)Math.Round(d.Percent))).ToList();
        _ = Task.Run(() =>
        {
            foreach (var (id, percent) in targets)
            {
                _brightness.Set(id, percent);
            }
        });
    }

    private void UpdateText() =>
        PercentText = Displays.Count == 0 ? "–" : $"{Displays.Average(d => d.Percent):0}%";

    public override void Dispose()
    {
        _apply.Stop();
        base.Dispose();
    }
}

/// <summary>The Pulse capsule: invisible until something needs attention.</summary>
public sealed partial class PulseWidget : WidgetViewModel
{
    private readonly PulseCenter _pulse;

    public PulseWidget(WidgetPlacement placement, PulseCenter pulse)
        : base(placement)
    {
        _pulse = pulse;
        _pulse.Changed += OnChanged;
        Update();
    }

    public override bool HasCard => true;

    public ObservableCollection<PulseRow> Rows { get; } = [];

    [ObservableProperty] private string _headline = string.Empty;
    [ObservableProperty] private string _glyph = WidgetCatalog.Glyph(0xE95E);
    [ObservableProperty] private string _countText = string.Empty;
    [ObservableProperty] private bool _isCritical;

    private void OnChanged(object? sender, EventArgs e) => Update();

    public override void ApplySettings(DockSettings settings) => Update();

    private void Update()
    {
        Rows.Clear();
        foreach (var a in _pulse.Alerts)
        {
            Rows.Add(new PulseRow(a.Id, WidgetCatalog.Glyph(a.GlyphCode), Loc.F(a.MessageKey, a.Args.Cast<object>().ToArray()), a.Severity, Dismiss));
        }

        var top = _pulse.Alerts.FirstOrDefault();
        IsVisibleOnBar = top is not null;
        Headline = Rows.FirstOrDefault()?.Message ?? string.Empty;
        Glyph = top is null ? WidgetCatalog.Glyph(0xE95E) : WidgetCatalog.Glyph(top.GlyphCode);
        CountText = _pulse.Alerts.Count > 1 ? $"+{_pulse.Alerts.Count - 1}" : string.Empty;
        IsCritical = top?.Severity == PulseSeverity.Critical;
    }

    private void Dismiss(string id) => _pulse.Dismiss(id);

    [RelayCommand]
    private void DismissAll() => _pulse.DismissAll();

    public override void Dispose()
    {
        _pulse.Changed -= OnChanged;
        base.Dispose();
    }
}

public sealed partial class PulseRow(string id, string glyph, string message, PulseSeverity severity, Action<string> dismiss) : ObservableObject
{
    public string Id { get; } = id;

    public string Glyph { get; } = glyph;

    public string Message { get; } = message;

    public bool IsCritical { get; } = severity == PulseSeverity.Critical;

    [RelayCommand]
    private void Dismiss() => dismiss(Id);
}
