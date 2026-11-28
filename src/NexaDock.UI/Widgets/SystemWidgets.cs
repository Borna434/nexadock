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

public sealed class ShowDesktopWidget(WidgetPlacement placement, IShellCommands shell) : WidgetViewModel(placement)
{
    public string Glyph { get; } = WidgetCatalog.Glyph(0xE7F4);

    public override void OnClick() => shell.ToggleDesktop();
}

/// <summary>A faint power glyph without a plate; one click shuts the computer down.</summary>
public sealed class PowerWidget(WidgetPlacement placement, IShellCommands shell) : WidgetViewModel(placement)
{
    public string Glyph { get; } = WidgetCatalog.Glyph(0xE7E8);

    public override bool HasPlate => false;

    public override void OnClick() => shell.ShutDown();
}

public sealed class SpacerWidget(WidgetPlacement placement) : WidgetViewModel(placement)
{
    public override bool IsFlexible => true;

    public override bool HasPlate => false;
}
