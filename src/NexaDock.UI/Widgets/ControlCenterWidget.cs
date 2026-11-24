using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;
using NexaDock.UI.Localization;
using NexaDock.UI.Services;
using NexaDock.UI.Theming;

namespace NexaDock.UI.Widgets;

public enum ControlPanel
{
    Network,
    Sound,
    Battery,
}

/// <summary>
/// The system icons. Network, sound and battery each open their own glass panel above the icon:
/// Wi-Fi networks and connections, output device and app mixer, battery and power mode. Scroll over
/// the icons for volume; the gear opens Windows' Quick Settings and the bell the notifications.
/// </summary>
public sealed partial class ControlCenterWidget : WidgetViewModel
{
    private readonly ISystemMonitor _monitor;
    private readonly IShellCommands _shell;
    private readonly IAudioService _audio;
    private readonly IWifiService _wifi;
    private readonly IRadioService _radios;
    private readonly IPowerService _power;
    private readonly IIconProvider _icons;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _cardTimer;
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private double _scale = 1;
    private bool _syncing;
    private DateTime _masterTouched;
    private int _cardTicks;
    private bool _scanning;

    public ControlCenterWidget(WidgetPlacement placement, ISystemMonitor monitor, IShellCommands shell, IAudioService audio,
        IWifiService wifi, IRadioService radios, IPowerService power, IIconProvider icons)
        : base(placement)
    {
        _monitor = monitor;
        _shell = shell;
        _audio = audio;
        _wifi = wifi;
        _radios = radios;
        _power = power;
        _icons = icons;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => { if (!QuietMode.IsActive) Update(); };
        _cardTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
        _cardTimer.Tick += async (_, _) => await RefreshPanelAsync(periodic: true);
    }

    public override bool HasCard => true;

    public override bool WantsKeyboard => true; // the Wi-Fi password field

    public string BellGlyph { get; } = WidgetCatalog.Glyph(0xEA8F);

    public string GearGlyph { get; } = WidgetCatalog.Glyph(0xE713);

    public string RefreshGlyph { get; } = WidgetCatalog.Glyph(0xE72C);

    [ObservableProperty] private string _volumeGlyph = WidgetCatalog.Glyph(0xE767);
    [ObservableProperty] private string _volumeText = string.Empty;
    [ObservableProperty] private string _networkGlyph = WidgetCatalog.Glyph(0xE701);
    [ObservableProperty] private string _networkText = string.Empty;
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private string _batteryGlyph = string.Empty;
    [ObservableProperty] private string _batteryText = string.Empty;

    /// <summary>Laptops show the battery; desktops a power icon for the power mode.</summary>
    [ObservableProperty] private bool _showPowerButton;

    public string PowerGlyph { get; } = WidgetCatalog.Glyph(0xE945);

    public string PowerTitle => Loc.T(HasBattery ? "cc.battery" : "cc.power");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNetworkPanel), nameof(IsSoundPanel), nameof(IsBatteryPanel))]
    private ControlPanel _panel = ControlPanel.Sound;

    public bool IsNetworkPanel => Panel == ControlPanel.Network;

    public bool IsSoundPanel => Panel == ControlPanel.Sound;

    public bool IsBatteryPanel => Panel == ControlPanel.Battery;

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

    public override void SetDpiScale(double scale)
    {
        _scale = scale;
        _iconCache.Clear();
    }

    public override void Start()
    {
        Update();
        ShowPowerButton = !HasBattery && _power.GetMode() is not null;
        _timer.Start();
    }

    private void Update()
    {
        var online = _monitor.IsNetworkAvailable();
        var wired = online && _wifi.GetConnections().FirstOrDefault() is { Kind: ConnectionKind.Ethernet };
        NetworkGlyph = WidgetCatalog.Glyph(!online ? 0xEB55 : wired ? 0xE839 : 0xE701);
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

    // ---- Opening the panels ------------------------------------------------------------------

    private void Show(ControlPanel panel)
    {
        if (IsCardOpen && Panel == panel)
        {
            RequestCloseCard();
            return;
        }

        Panel = panel;
        RequestCard();
    }

    [RelayCommand]
    private void OpenNetwork() => Show(ControlPanel.Network);

    [RelayCommand]
    private void OpenSound() => Show(ControlPanel.Sound);

    [RelayCommand]
    private void OpenBattery() => Show(ControlPanel.Battery);

    [RelayCommand]
    private void OpenQuickSettings() => _shell.OpenQuickSettings();

    [RelayCommand]
    private void OpenNotifications() => _shell.OpenNotificationCenter();

    public override async void OnCardOpened()
    {
        _cardTicks = 0;
        _cardTimer.Start();
        await RefreshPanelAsync(periodic: false);
    }

    public override void OnCardClosed()
    {
        _cardTimer.Stop();
        foreach (var row in Networks)
        {
            row.Collapse();
        }
    }

    private async Task RefreshPanelAsync(bool periodic)
    {
        _cardTicks++;
        switch (Panel)
        {
            case ControlPanel.Network:
                // Re-read every few seconds, ask the adapter for a fresh scan only now and then.
                if (!periodic || _cardTicks % 4 == 0)
                {
                    await RefreshNetworkAsync(rescan: !periodic || _cardTicks % 12 == 0);
                }

                break;
            case ControlPanel.Sound:
                RefreshSound();
                break;
            case ControlPanel.Battery:
                RefreshBattery();
                break;
        }
    }

    // ---- Network -----------------------------------------------------------------------------

    public ObservableCollection<ConnectionRow> Connections { get; } = [];

    public ObservableCollection<WifiRow> Networks { get; } = [];

    [ObservableProperty] private bool _hasWifi;
    [ObservableProperty] private bool _wifiOn;
    [ObservableProperty] private string _wifiMessage = string.Empty;
    [ObservableProperty] private bool _needsLocation;

    private async Task RefreshNetworkAsync(bool rescan)
    {
        Sync(Connections, _wifi.GetConnections().Select(c => new ConnectionRow(
            WidgetCatalog.Glyph(c.Kind == ConnectionKind.Ethernet ? 0xE839 : c.Kind == ConnectionKind.Wifi ? 0xE701 : 0xE774),
            c.Kind == ConnectionKind.Ethernet ? Loc.T("cc.ethernet") : c.Kind == ConnectionKind.Wifi ? "Wi-Fi" : c.Name,
            string.Join(" · ", new[] { Speed(c.SpeedBitsPerSecond), c.Address }.Where(s => !string.IsNullOrEmpty(s))))).ToList());

        var radios = await _radios.GetRadiosAsync();
        var radio = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
        WifiOn = radio?.IsOn ?? true;

        if (_scanning)
        {
            return;
        }

        _scanning = true;
        try
        {
            if (Networks.Count == 0 && WifiOn)
            {
                WifiMessage = Loc.T("cc.scanning");
            }

            var scan = await _wifi.GetNetworksAsync(rescan && WifiOn);
            HasWifi = scan.Access != WifiAccess.NoAdapter || radio is not null;
            NeedsLocation = scan.Access == WifiAccess.LocationDenied;
            if (!WifiOn)
            {
                Networks.Clear();
                WifiMessage = Loc.T("cc.wifiOff");
                return;
            }

            UpdateNetworks(scan.Networks);
            WifiMessage = scan.Access switch
            {
                WifiAccess.LocationDenied => Loc.T("cc.location"),
                WifiAccess.NoAdapter => Loc.T("cc.noAdapter"),
                _ when Networks.Count == 0 => Loc.T("cc.noNetworks"),
                _ => string.Empty,
            };
        }
        finally
        {
            _scanning = false;
        }
    }

    private void UpdateNetworks(IReadOnlyList<WifiNetwork> found)
    {
        var shown = found.Take(12).ToList();
        for (var i = Networks.Count - 1; i >= 0; i--)
        {
            if (!Networks[i].IsBusy && !Networks[i].IsExpanded && shown.All(n => n.Ssid != Networks[i].Ssid))
            {
                Networks.RemoveAt(i);
            }
        }

        for (var i = 0; i < shown.Count; i++)
        {
            var existing = Networks.FirstOrDefault(r => r.Ssid == shown[i].Ssid);
            if (existing is null)
            {
                Networks.Insert(Math.Min(i, Networks.Count), new WifiRow(this, shown[i]));
            }
            else
            {
                existing.Update(shown[i]);
                var at = Networks.IndexOf(existing);
                if (at != i && i < Networks.Count && !existing.IsExpanded)
                {
                    Networks.Move(at, i);
                }
            }
        }
    }

    internal void Expand(WifiRow row)
    {
        var open = !row.IsExpanded;
        foreach (var r in Networks)
        {
            r.Collapse();
        }

        row.IsExpanded = open;
    }

    internal async Task ConnectAsync(WifiRow row, PasswordBox? box)
    {
        if (row.Network.Security == WifiSecurity.Enterprise && row.Network.ProfileName is null)
        {
            row.Status = Loc.T("cc.enterprise");
            _shell.OpenNetworkFlyout();
            return;
        }

        var password = box?.Password;
        if (row.NeedsPassword && string.IsNullOrEmpty(password))
        {
            box?.Focus();
            return;
        }

        row.IsBusy = true;
        row.Status = Loc.T("cc.connecting");
        var result = await _wifi.ConnectAsync(row.Network, password);
        box?.Clear();
        row.IsBusy = false;
        switch (result)
        {
            case WifiConnectResult.Connected:
                row.Status = string.Empty;
                row.Collapse();
                break;
            case WifiConnectResult.Unsupported:
                row.Status = Loc.T("cc.enterprise");
                _shell.OpenNetworkFlyout();
                break;
            default:
                row.Status = Loc.T("cc.connectFailed");
                break;
        }

        await RefreshNetworkAsync(rescan: false);
        Update();
    }

    internal async Task DisconnectAsync()
    {
        await _wifi.DisconnectAsync();
        await Task.Delay(800);
        await RefreshNetworkAsync(rescan: false);
        Update();
    }

    [RelayCommand]
    private async Task ToggleWifi()
    {
        await _radios.SetAsync(RadioKind.WiFi, !WifiOn);
        await Task.Delay(1200); // the adapter needs a moment before it can scan
        await RefreshNetworkAsync(rescan: true);
        Update();
    }

    [RelayCommand]
    private Task Rescan() => RefreshNetworkAsync(rescan: true);

    [RelayCommand]
    private void AllowLocation() => _shell.OpenSystemUri("ms-settings:privacy-location");

    [RelayCommand]
    private void WifiSettings() => _shell.OpenSystemUri("ms-settings:network-wifi");

    [RelayCommand]
    private void WindowsNetworks() => _shell.OpenNetworkFlyout();

    private static string Speed(long bits) => bits switch
    {
        <= 0 => string.Empty,
        >= 1_000_000_000 => $"{bits / 1_000_000_000.0:0.#} Gb/s",
        _ => $"{bits / 1_000_000.0:0} Mb/s",
    };

    // ---- Sound -------------------------------------------------------------------------------

    public ObservableCollection<OutputRow> Outputs { get; } = [];

    public ObservableCollection<AppAudioRow> Apps { get; } = [];

    [ObservableProperty] private double _masterVolume;
    [ObservableProperty] private bool _masterMuted;
    [ObservableProperty] private string _masterGlyph = WidgetCatalog.Glyph(0xE767);
    [ObservableProperty] private bool _hasApps;

    partial void OnMasterVolumeChanged(double value)
    {
        if (_syncing)
        {
            return;
        }

        _masterTouched = DateTime.UtcNow;
        _audio.SetSpeakerVolume(value / 100);
        if (MasterMuted && value > 0)
        {
            _audio.SetSpeakerMuted(false);
        }

        UpdateVolume();
        MasterGlyph = VolumeGlyph;
    }

    [RelayCommand]
    private void ToggleMasterMute()
    {
        _audio.SetSpeakerMuted(!MasterMuted);
        RefreshSound();
        UpdateVolume();
    }

    private void RefreshSound()
    {
        var speaker = _audio.GetSpeaker();
        _syncing = true;
        if (DateTime.UtcNow - _masterTouched > TimeSpan.FromSeconds(1))
        {
            MasterVolume = Math.Round(speaker.Volume * 100);
        }

        MasterMuted = speaker.Muted;
        _syncing = false;
        UpdateVolume();
        MasterGlyph = VolumeGlyph;

        var outputs = _audio.GetOutputDevices();
        if (outputs.Count != Outputs.Count || outputs.Where((o, i) => Outputs[i].Id != o.Id || Outputs[i].IsDefault != o.IsDefault).Any())
        {
            Outputs.Clear();
            foreach (var o in outputs)
            {
                Outputs.Add(new OutputRow(this, o.Id, o.Name, o.IsDefault, DeviceGlyph(o.Name)));
            }
        }

        var sessions = _audio.GetAppSessions();
        for (var i = Apps.Count - 1; i >= 0; i--)
        {
            if (sessions.All(s => s.Key != Apps[i].Key))
            {
                Apps.RemoveAt(i);
            }
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var row = Apps.FirstOrDefault(a => a.Key == s.Key);
            if (row is null)
            {
                var name = s.IsSystemSounds ? Loc.T("cc.systemSounds") : s.Name;
                row = new AppAudioRow(this, s.Key, name, IconFor(s.ExePath));
                Apps.Insert(Math.Min(i, Apps.Count), row);
            }

            row.Sync(s.Volume, s.Muted);
        }

        HasApps = Apps.Count > 0;
    }

    internal void SetOutput(OutputRow row)
    {
        if (row.IsDefault)
        {
            return;
        }

        _audio.SetDefaultOutput(row.Id);
        Outputs.Clear(); // re-read with the new default
        RefreshSound();
    }

    internal void SetAppVolume(AppAudioRow row, double percent) => _audio.SetSessionVolume(row.Key, percent / 100);

    internal void SetAppMuted(AppAudioRow row, bool muted) => _audio.SetSessionMuted(row.Key, muted);

    [RelayCommand]
    private void SoundSettings() => _shell.OpenSystemUri("ms-settings:sound");

    private ImageSource? IconFor(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (!_iconCache.TryGetValue(path, out var icon))
        {
            icon = _icons.GetIcon(path, IconImageExtensions.IconPixelSize(18, _scale)).ToBitmapSource();
            _iconCache[path] = icon;
        }

        return icon;
    }

    private static string DeviceGlyph(string name)
    {
        var n = name.ToLowerInvariant();
        return WidgetCatalog.Glyph(n.Contains("headphone") || n.Contains("headset") || n.Contains("slušal") || n.Contains("buds") || n.Contains("airpods")
            ? 0xE7F6
            : n.Contains("hdmi") || n.Contains("display") || n.Contains("monitor") ? 0xE7F4
            : 0xE7F5);
    }

    // ---- Battery and power mode --------------------------------------------------------------

    [ObservableProperty] private int _batteryPercent;
    [ObservableProperty] private string _batteryStatus = string.Empty;
    [ObservableProperty] private bool _saverOn;
    [ObservableProperty] private bool _hasPowerModes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEfficiency), nameof(IsBalanced), nameof(IsPerformance))]
    private PowerMode? _powerMode;

    public bool IsEfficiency => PowerMode == Core.Abstractions.PowerMode.Efficiency;

    public bool IsBalanced => PowerMode == Core.Abstractions.PowerMode.Balanced;

    public bool IsPerformance => PowerMode == Core.Abstractions.PowerMode.Performance;

    private void RefreshBattery()
    {
        var b = _power.GetBattery();
        OnPropertyChanged(nameof(PowerTitle));
        BatteryPercent = b.Percent;
        SaverOn = b.SaverOn;
        BatteryStatus = b.IsCharging ? Loc.T("cc.charging")
            : b.PluggedIn ? Loc.T("cc.pluggedIn")
            : b.Remaining is { } left ? Loc.F("cc.remaining", (int)left.TotalHours, left.Minutes)
            : Loc.T("cc.onBattery");
        PowerMode = _power.GetMode();
        HasPowerModes = PowerMode is not null;
    }

    [RelayCommand]
    private void SetPowerMode(string mode)
    {
        if (Enum.TryParse<PowerMode>(mode, out var m) && _power.SetMode(m))
        {
            PowerMode = m;
        }
    }

    [RelayCommand]
    private void PowerSettings() => _shell.OpenSystemUri("ms-settings:powersleep");

    // ---- Helpers -----------------------------------------------------------------------------

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        if (target.SequenceEqual(source))
        {
            return;
        }

        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    public override void Dispose()
    {
        _timer.Stop();
        _cardTimer.Stop();
        base.Dispose();
    }
}

public sealed record ConnectionRow(string Glyph, string Title, string Detail);

/// <summary>One Wi-Fi network in the network panel; click to expand, then connect.</summary>
public sealed partial class WifiRow : ObservableObject
{
    private readonly ControlCenterWidget _owner;

    public WifiRow(ControlCenterWidget owner, WifiNetwork network)
    {
        _owner = owner;
        _network = network;
        Update(network);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ssid), nameof(NeedsPassword), nameof(IsSecured), nameof(IsConnected))]
    private WifiNetwork _network;

    [ObservableProperty] private string _signalGlyph = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = string.Empty;

    public string Ssid => Network.Ssid;

    public bool IsConnected => Network.IsConnected;

    public bool IsSecured => Network.Security != WifiSecurity.Open;

    /// <summary>A secured network that was never joined: the expanded row shows a password field.</summary>
    public bool NeedsPassword => Network.ProfileName is null && Network.Security == WifiSecurity.Personal;

    public string LockGlyph { get; } = WidgetCatalog.Glyph(0xE72E);

    public void Update(WifiNetwork network)
    {
        Network = network;
        SignalGlyph = WidgetCatalog.Glyph(network.SignalPercent >= 75 ? 0xE701 : network.SignalPercent >= 50 ? 0xE874 : network.SignalPercent >= 25 ? 0xE873 : 0xE872);
        var parts = new List<string>();
        if (network.IsConnected)
        {
            parts.Add(Loc.T("cc.connected"));
        }
        else if (network.ProfileName is not null)
        {
            parts.Add(Loc.T("cc.saved"));
        }

        parts.Add(Loc.T(IsSecured ? "cc.secured" : "cc.open"));
        var detail = string.Join(", ", parts);
        Detail = detail.Length > 0 ? char.ToUpper(detail[0]) + detail[1..] : detail;
    }

    public void Collapse()
    {
        IsExpanded = false;
        if (!IsBusy)
        {
            Status = string.Empty;
        }
    }

    [RelayCommand]
    private void Toggle() => _owner.Expand(this);

    [RelayCommand]
    private Task Connect(object? passwordBox) => _owner.ConnectAsync(this, passwordBox as PasswordBox);

    [RelayCommand]
    private Task Disconnect() => _owner.DisconnectAsync();
}

public sealed partial class OutputRow(ControlCenterWidget owner, string id, string name, bool isDefault, string glyph) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public bool IsDefault { get; } = isDefault;

    public string Glyph { get; } = glyph;

    [RelayCommand]
    private void Select() => owner.SetOutput(this);
}

/// <summary>One app in the sound panel's mixer.</summary>
public sealed partial class AppAudioRow : ObservableObject
{
    private readonly ControlCenterWidget _owner;
    private bool _syncing;
    private DateTime _touched;

    public AppAudioRow(ControlCenterWidget owner, string key, string name, ImageSource? icon)
    {
        _owner = owner;
        Key = key;
        Name = name;
        Icon = icon;
    }

    public string Key { get; }

    public string Name { get; }

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string FallbackGlyph { get; } = WidgetCatalog.Glyph(0xE767);

    [ObservableProperty] private double _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteGlyph))]
    private bool _muted;

    public string MuteGlyph => WidgetCatalog.Glyph(Muted ? 0xE74F : 0xE767);

    public void Sync(double volume01, bool muted)
    {
        if (DateTime.UtcNow - _touched < TimeSpan.FromSeconds(1))
        {
            return; // the user is dragging this slider
        }

        _syncing = true;
        Volume = Math.Round(volume01 * 100);
        Muted = muted;
        _syncing = false;
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_syncing)
        {
            _touched = DateTime.UtcNow;
            _owner.SetAppVolume(this, value);
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        Muted = !Muted;
        _owner.SetAppMuted(this, Muted);
    }
}
