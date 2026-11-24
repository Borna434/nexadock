using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

using NexaDock.UI.Localization;

using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

/// <summary>CPU and memory rings; the card lists the busiest apps.</summary>
public sealed partial class SystemWidget : WidgetViewModel
{
    private readonly ISystemMonitor _monitor;
    private readonly IShellCommands _shell;
    private readonly DispatcherTimer _timer;

    public SystemWidget(WidgetPlacement placement, ISystemMonitor monitor, IShellCommands shell)
        : base(placement)
    {
        _monitor = monitor;
        _shell = shell;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Update(); };
    }

    public override bool HasCard => true;

    [ObservableProperty] private double _cpu;
    [ObservableProperty] private double _memory;
    [ObservableProperty] private string _cpuText = "–";
    [ObservableProperty] private string _memoryText = "–";
    [ObservableProperty] private string _memoryDetail = string.Empty;
    [ObservableProperty] private string _uptime = string.Empty;

    public ObservableCollection<ProcessUsage> TopCpu { get; } = [];

    public ObservableCollection<ProcessUsage> TopMemory { get; } = [];

    public override void Start()
    {
        _monitor.Sample(); // prime the CPU delta
        _timer.Start();
    }

    private void Update()
    {
        var s = _monitor.Sample();
        Cpu = s.CpuPercent;
        Memory = s.MemoryPercent;
        CpuText = $"{s.CpuPercent:0}%";
        MemoryText = $"{s.MemoryPercent:0}%";
        MemoryDetail = Loc.F("system.memoryOf", s.MemoryUsedBytes / 1073741824.0, s.MemoryTotalBytes / 1073741824.0);
        Uptime = s.Uptime.TotalDays >= 1 ? Loc.F("system.uptimeDays", (int)s.Uptime.TotalDays, s.Uptime.Hours) : Loc.F("system.uptimeHours", s.Uptime.Hours, s.Uptime.Minutes);

        if (IsCardOpen)
        {
            Replace(TopCpu, _monitor.TopByCpu(5));
            Replace(TopMemory, _monitor.TopByMemory(5));
        }
    }

    public override void OnCardOpened()
    {
        _monitor.TopByCpu(5); // establish a baseline so the first refresh shows real numbers
        Replace(TopMemory, _monitor.TopByMemory(5));
    }

    private static void Replace(ObservableCollection<ProcessUsage> target, IReadOnlyList<ProcessUsage> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }

    [RelayCommand]
    private void OpenTaskManager() => _shell.OpenTaskManager();

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

/// <summary>
/// The system icons, each with its own Windows flyout like on the taskbar: network (Wi-Fi list),
/// sound (output and volume; scroll to change the volume), battery (Quick Settings) and
/// notifications.
/// </summary>
public sealed partial class ControlCenterWidget : WidgetViewModel
{
    private readonly ISystemMonitor _monitor;
    private readonly IShellCommands _shell;
    private readonly IAudioService _audio;
    private readonly DispatcherTimer _timer;

    public ControlCenterWidget(WidgetPlacement placement, ISystemMonitor monitor, IShellCommands shell, IAudioService audio)
        : base(placement)
    {
        _monitor = monitor;
        _shell = shell;
        _audio = audio;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Update(); };
    }

    public string BellGlyph { get; } = WidgetCatalog.Glyph(0xEA8F);

    [ObservableProperty] private string _volumeGlyph = WidgetCatalog.Glyph(0xE767);
    [ObservableProperty] private string _volumeText = string.Empty;

    public override bool HandlesWheel => true;

    /// <summary>Mouse wheel over the icons changes the volume, as on the Windows taskbar.</summary>
    public override void OnWheel(int delta)
    {
        var speaker = _audio.GetSpeaker();
        if (!speaker.Available)
        {
            return;
        }

        _audio.SetSpeakerVolume(Math.Clamp(speaker.Volume + Math.Sign(delta) * 0.02, 0, 1));
        UpdateVolume();
    }

    [ObservableProperty] private string _networkGlyph = WidgetCatalog.Glyph(0xE701);
    [ObservableProperty] private string _networkText = string.Empty;
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private string _batteryGlyph = string.Empty;
    [ObservableProperty] private string _batteryText = string.Empty;

    public override void Start()
    {
        Update();
        _timer.Start();
    }

    private void Update()
    {
        var online = _monitor.IsNetworkAvailable();
        NetworkGlyph = WidgetCatalog.Glyph(online ? 0xE701 : 0xEB55);
        NetworkText = Loc.T(online ? "control.connected" : "control.offline");

        UpdateVolume();

        var power = _monitor.GetPower();
        HasBattery = power.HasBattery;
        if (power.HasBattery)
        {
            BatteryGlyph = WidgetCatalog.Glyph(0xE850 + Math.Clamp(power.Percent / 10, 0, 10));
            BatteryText = power.IsCharging ? $"{power.Percent}% ⚡" : $"{power.Percent}%";
        }
    }

    private void UpdateVolume()
    {
        var speaker = _audio.GetSpeaker();
        var level = (int)Math.Round(speaker.Volume * 100);
        VolumeGlyph = WidgetCatalog.Glyph(!speaker.Available || speaker.Muted ? 0xE74F
            : level == 0 ? 0xE992
            : level < 34 ? 0xE993
            : level < 67 ? 0xE994
            : 0xE995);
        VolumeText = !speaker.Available ? Loc.T("control.noAudio")
            : speaker.Muted ? Loc.T("control.muted")
            : Loc.F("control.volume", level);
    }

    public override void OnClick() => _shell.OpenQuickSettings();

    [RelayCommand]
    private void OpenNetwork() => _shell.OpenNetworkFlyout();

    [RelayCommand]
    private void OpenSound() => _shell.OpenSoundFlyout();

    [RelayCommand]
    private void OpenQuickSettings() => _shell.OpenQuickSettings();

    [RelayCommand]
    private void OpenNotifications() => _shell.OpenNotificationCenter();

    public override void Dispose()
    {
        _timer.Stop();
        base.Dispose();
    }
}

public sealed class ShowDesktopWidget(WidgetPlacement placement, IShellCommands shell) : WidgetViewModel(placement)
{
    public string Glyph { get; } = WidgetCatalog.Glyph(0xE7F4);

    public override void OnClick() => shell.ToggleDesktop();
}

public sealed class SpacerWidget(WidgetPlacement placement) : WidgetViewModel(placement)
{
    public override bool IsFlexible => true;

    public override bool HasPlate => false;
}
