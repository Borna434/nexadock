using NexaDock.Core.Models;

namespace NexaDock.Core.Abstractions;

/// <summary>Outcome of reading persisted settings.</summary>
public enum SettingsLoadStatus
{
    /// <summary>Stored settings were read successfully (possibly with individual fields corrected).</summary>
    Loaded,

    /// <summary>No settings existed yet; defaults are returned.</summary>
    NotFound,

    /// <summary>Storage was unreadable or corrupt; defaults are returned and the bad data was set aside.</summary>
    Recovered,
}

public sealed record SettingsLoadResult(DockSettings Settings, SettingsLoadStatus Status, string? Detail = null);

/// <summary>Durable storage for <see cref="DockSettings"/>.</summary>
public interface ISettingsStore
{
    SettingsLoadResult Load();

    void Save(DockSettings settings);
}

/// <summary>In-memory owner of the current settings with change notification and deferred saving.</summary>
public interface ISettingsService
{
    DockSettings Current { get; }

    SettingsLoadStatus LoadStatus { get; }

    /// <summary>Raised on the thread that called <see cref="Update"/> after a real change.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    void Load();

    void Update(Func<DockSettings, DockSettings> mutate);

    /// <summary>Writes pending changes synchronously.</summary>
    void Flush();
}

public sealed class SettingsChangedEventArgs(DockSettings previous, DockSettings current) : EventArgs
{
    public DockSettings Previous { get; } = previous;
    public DockSettings Current { get; } = current;
}

// ---- Placement ------------------------------------------------------------------------------------

/// <summary>Enumerates displays.</summary>
public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}

/// <param name="WindowPadding">Transparent margin (DIPs) around the glass, e.g. for a shadow.</param>
/// <param name="UseMonitorBounds">Place against the full monitor instead of the work area (taskbar replaced).</param>
public readonly record struct PlacementOptions(double WindowPadding = 0, bool UseMonitorBounds = false);

/// <summary>Computes where the bar window goes.</summary>
public interface IDockPositioner
{
    /// <param name="contentWidth">Width (DIPs) the widgets need; used when the bar is not full width.</param>
    PixelRect Calculate(MonitorInfo monitor, DockSettings settings, double contentWidth, PlacementOptions options = default);
}

public sealed record BackdropCapabilities(bool SupportsSystemBackdrop, int OsBuild, string Description);

public sealed record BackdropOptions(bool DarkMode);

/// <summary>Native backdrop support (DWM system backdrop on Windows 11 22H2+).</summary>
public interface IBackdropService
{
    BackdropCapabilities Capabilities { get; }

    /// <summary>The kind windows should be created with (decided before the window exists).</summary>
    BackdropKind PreferredKind { get; }

    /// <summary>Applies the native backdrop. Returns false when DWM rejected it.</summary>
    bool TryApply(IntPtr hwnd, BackdropOptions options);

    void SetDarkMode(IntPtr hwnd, bool dark);

    /// <summary>Rounded window corners (floating bar) or square ones (bar flush with the edges).</summary>
    void SetRoundedCorners(IntPtr hwnd, bool rounded);
}

/// <summary>Text or an image that was copied.</summary>
public sealed record ClipCapture(string? Text, byte[]? Png, int Width, int Height);

/// <summary>Watches the clipboard and puts entries back on it.</summary>
public interface IClipboardMonitor
{
    event EventHandler<ClipCapture>? Captured;

    void Start();

    void Stop();

    /// <summary>Puts text on the clipboard without recording it as a new copy.</summary>
    void SetText(string text);

    void SetImage(byte[] png);
}

/// <summary>The system-wide shortcuts: the launcher and the clipboard history.</summary>
public interface IGlobalHotkey
{
    event EventHandler? Pressed;

    event EventHandler? ClipboardPressed;

    /// <summary>Win+Alt+V for the clipboard history; false when another app has it.</summary>
    bool RegisterClipboard(bool enabled);

    /// <summary>False when another app already uses the combination.</summary>
    bool Register(LauncherHotkey hotkey);

    /// <summary>Whether the last registration succeeded (false while off or taken).</summary>
    bool IsRegistered { get; }

    void Unregister();
}

/// <summary>Copies a small region of the screen (for the refraction rim).</summary>
public interface IScreenStripCapture
{
    /// <summary>Fills <paramref name="bgra"/> (width × height × 4, top-down) from the screen; false on failure.</summary>
    bool Capture(PixelRect region, byte[] bgra);
}

/// <summary>Native window behaviour for the bar and its pop-ups.</summary>
public interface IDockWindowHost
{
    /// <summary>
    /// Makes the window non-activating and hidden from Alt+Tab. When <paramref name="isMainBar"/> is
    /// true, also listens for display, DPI and work-area changes.
    /// </summary>
    void Attach(IntPtr hwnd, bool isMainBar);

    void SetBounds(IntPtr hwnd, PixelRect bounds);

    /// <summary>Registers (or updates) the bar as an app bar that reserves <paramref name="strip"/>.</summary>
    void SetAppBar(IntPtr hwnd, DockEdge edge, PixelRect strip);

    void RemoveAppBar(IntPtr hwnd);

    PointerState GetPointer();

    /// <summary>
    /// Lets a pop-up take keyboard focus (for typing a to-do or note) or returns it to the
    /// non-activating mode.
    /// </summary>
    void SetKeyboardInput(IntPtr hwnd, bool enabled);

    /// <summary>
    /// Reports every mouse-button press anywhere on screen (physical pixels) until disposed.
    /// Used to close pop-ups on outside clicks, since non-activating windows get no deactivation.
    /// </summary>
    IDisposable ObserveMouseDown(Action<PointerState> onDown);

    /// <summary>Window bounds in physical pixels.</summary>
    PixelRect GetBounds(IntPtr hwnd);

    /// <summary>Raised (on the UI thread) when displays, DPI or the work area changed.</summary>
    event EventHandler? PlacementInvalidated;
}

// ---- Apps and windows -----------------------------------------------------------------------------

/// <summary>A top-level window that would appear on the taskbar.</summary>
public sealed record TrackedWindow(
    IntPtr Handle,
    string Title,
    int ProcessId,
    string? ExecutablePath,
    string? AppUserModelId,
    bool IsMinimized,
    bool IsForeground);

/// <summary>Live list of taskbar-eligible windows.</summary>
public interface IWindowTracker
{
    IReadOnlyList<TrackedWindow> Windows { get; }

    IntPtr ForegroundWindow { get; }

    /// <summary>Raised on the UI thread when windows open, close, change title or focus moves.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// The window in front changed its size or position, e.g. a video switching to full screen in
    /// the same browser window (no window opens or closes, so <see cref="Changed"/> stays silent).
    /// </summary>
    event EventHandler? ForegroundMoved;

    void Start();

    void Activate(IntPtr hwnd);

    void Minimize(IntPtr hwnd);

    void Close(IntPtr hwnd);

    /// <summary>True when the foreground window covers the whole given monitor (game, video, slideshow).</summary>
    bool IsForegroundFullscreen(MonitorInfo monitor);

    /// <summary>GDI name of the display a window is on (most of its area), e.g. \\.\DISPLAY2.</summary>
    string? GetMonitorDeviceName(IntPtr hwnd);
}

/// <summary>Windows shell actions that the taskbar normally provides.</summary>
public interface IShellCommands
{
    void OpenStartMenu();

    void OpenQuickSettings();

    void OpenNotificationCenter();

    void ToggleDesktop();

    void OpenTaskManager();

    void OpenDateTimeSettings();

    /// <summary>Windows' own Wi-Fi list (the network flyout).</summary>
    void OpenNetworkFlyout();

    /// <summary>Windows' own sound flyout: output device and volume (Win+Ctrl+V).</summary>
    void OpenSoundFlyout();

    /// <summary>Opens a Settings page or other system URI (ms-settings:sound, ...).</summary>
    void OpenSystemUri(string uri);

    /// <summary>Brings a window to the front and presses Ctrl+V in it.</summary>
    void PasteInto(IntPtr window);
}

/// <summary>Raw 32-bit BGRA (premultiplied) pixels for an icon.</summary>
public sealed record IconImage(int Width, int Height, byte[] Pixels);

public interface IIconProvider
{
    IconImage? GetIcon(string target, int pixelSize);
}

public sealed record LaunchResult(bool Success, string? Error = null);

public interface IAppLauncher
{
    LaunchResult Launch(DockShortcut shortcut);
}

/// <summary>Discovers sensible first-run shortcuts and describes user-picked files.</summary>
public interface IShortcutCatalog
{
    IReadOnlyList<DockShortcut> GetDefaultShortcuts();

    DockShortcut CreateFromFile(string path);

    /// <summary>What a pinned shortcut launches, used to match it with running windows.</summary>
    ShortcutIdentity Identify(DockShortcut shortcut);

    /// <summary>Friendly name of an executable (its file description), or the file name.</summary>
    string GetDisplayName(string executablePath);
}

/// <summary>Executable path and/or packaged-app ID a shortcut resolves to.</summary>
public sealed record ShortcutIdentity(string? ExecutablePath, string? AppUserModelId)
{
    public bool Matches(TrackedWindow window) =>
        (AppUserModelId is not null && string.Equals(AppUserModelId, window.AppUserModelId, StringComparison.OrdinalIgnoreCase))
        || (ExecutablePath is not null && string.Equals(ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Cursor position (physical pixels) and whether a mouse button is held.</summary>
public readonly record struct PointerState(int X, int Y, bool AnyButtonDown);

/// <summary>Live window thumbnails rendered by DWM into one of our windows.</summary>
public interface IThumbnailService
{
    /// <summary>Shows <paramref name="source"/> scaled into <paramref name="destination"/> (pixels, client coordinates).</summary>
    IntPtr Register(IntPtr hostWindow, IntPtr source, PixelRect destination);

    /// <summary>Size of the source window in pixels.</summary>
    (int Width, int Height) GetSourceSize(IntPtr thumbnail);

    void Unregister(IntPtr thumbnail);
}

// ---- Live data ------------------------------------------------------------------------------------

public sealed record ProcessUsage(string Name, double Value, string Display);

public sealed record SystemSnapshot(
    double CpuPercent,
    double MemoryPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    TimeSpan Uptime);

public sealed record PowerStatus(bool HasBattery, int Percent, bool IsCharging);

public interface ISystemMonitor
{
    /// <summary>Samples CPU (since the previous call) and memory.</summary>
    SystemSnapshot Sample();

    IReadOnlyList<ProcessUsage> TopByMemory(int count);

    /// <summary>Busiest processes by CPU since the previous call to this method.</summary>
    IReadOnlyList<ProcessUsage> TopByCpu(int count);

    PowerStatus GetPower();

    bool IsNetworkAvailable();
}

public sealed record WeatherDay(DateOnly Date, double Max, double Min, int Code);

public sealed record WeatherReport(
    string Location,
    double Temperature,
    double ApparentTemperature,
    int Code,
    double WindSpeed,
    int Humidity,
    bool IsDay,
    IReadOnlyList<WeatherDay> Days,
    DateTimeOffset FetchedAt);

public interface IWeatherService
{
    /// <exception cref="WeatherException">The city was not found or the service failed.</exception>
    Task<WeatherReport> GetAsync(string city, CancellationToken cancellationToken);
}

public sealed class WeatherException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record MediaInfo(
    string Title,
    string Artist,
    string Source,
    bool IsPlaying,
    byte[]? Thumbnail,
    TimeSpan Position,
    TimeSpan Duration,
    bool CanPrevious,
    bool CanNext,
    bool CanSeek = false);

/// <summary>Whatever app reports playback to Windows (System Media Transport Controls).</summary>
public interface IMediaService
{
    MediaInfo? Current { get; }

    /// <summary>Raised on the UI thread when the track or playback state changes.</summary>
    event EventHandler? Changed;

    Task StartAsync();

    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    /// <summary>Jumps to a position in the current track.</summary>
    Task SeekAsync(TimeSpan position);
}

// ---- Taskbar & startup ----------------------------------------------------------------------------

/// <summary>Hides and restores the Windows taskbar (never modifies Windows files).</summary>
public interface ITaskbarController
{
    /// <summary>Current taskbar app-bar state flags (ABS_*), for later restore.</summary>
    int GetState();

    void Hide();

    /// <summary>Shows the taskbar again and restores the given state flags.</summary>
    void Restore(int state);
}

public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

// ---- Phase 3 services ------------------------------------------------------------------------------

/// <summary>Small local key/value store for widget content (to-dos, notes). Values are JSON/text.</summary>
public interface IKeyValueStore
{
    string? Get(string key);

    void Set(string key, string? value);

    /// <summary>Every stored value, by key (for backups).</summary>
    IReadOnlyDictionary<string, string> GetAll();
}

public sealed record AudioEndpointState(bool Available, string DeviceName, double Volume, bool Muted);

/// <summary>Default speaker and microphone through Windows Core Audio.</summary>
public interface IAudioService
{
    AudioEndpointState GetSpeaker();

    AudioEndpointState GetMicrophone();

    void SetSpeakerVolume(double volume01);

    void SetSpeakerMuted(bool muted);

    void SetMicrophoneMuted(bool muted);

    /// <summary>
    /// Volume of the app that plays (matched by name, e.g. "Spotify" or "Chrome"); null when the
    /// app has no audio session right now.
    /// </summary>
    double? GetAppVolume(string appHint);

    void SetAppVolume(string appHint, double volume01);

    /// <summary>Active output devices (speakers, headphones, monitors); the default one is marked.</summary>
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    /// <summary>Makes a device the default output for every role, like the Windows sound flyout.</summary>
    bool SetDefaultOutput(string deviceId);

    /// <summary>Apps with sound on the default output, one row per app (all of its processes).</summary>
    IReadOnlyList<AudioAppSession> GetAppSessions();

    void SetSessionVolume(string key, double volume01);

    void SetSessionMuted(string key, bool muted);
}

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <param name="Key">Process name (lower case), or <see cref="SystemSoundsKey"/>.</param>
/// <param name="Name">Friendly app name; empty for Windows' system sounds.</param>
/// <param name="ExePath">For the app icon; null when unknown.</param>
public sealed record AudioAppSession(string Key, string Name, string? ExePath, double Volume, bool Muted, bool IsPlaying)
{
    public const string SystemSoundsKey = "#system";

    public bool IsSystemSounds => Key == SystemSoundsKey;
}

public enum WifiSecurity
{
    Open,
    Personal,
    Enterprise,
}

/// <param name="ProfileName">The saved Windows profile, or null when the network was never joined.</param>
public sealed record WifiNetwork(string Ssid, int SignalPercent, WifiSecurity Security, bool IsConnected, string? ProfileName);

public enum WifiAccess
{
    Ok,

    /// <summary>No Wi-Fi adapter (a desktop on Ethernet).</summary>
    NoAdapter,

    /// <summary>Windows hides network names from apps until location access is allowed.</summary>
    LocationDenied,

    Unavailable,
}

public sealed record WifiScan(WifiAccess Access, IReadOnlyList<WifiNetwork> Networks);

public enum ConnectionKind
{
    Ethernet,
    Wifi,
    Other,
}

/// <summary>A network connection that is up and has a gateway.</summary>
public sealed record ConnectionInfo(ConnectionKind Kind, string Name, string Adapter, long SpeedBitsPerSecond, string? Address);

public enum WifiConnectResult
{
    Connected,
    NeedsPassword,
    Failed,
    Unsupported,
}

/// <summary>Wi-Fi networks through the Native Wifi API, plus a summary of active connections.</summary>
public interface IWifiService
{
    IReadOnlyList<ConnectionInfo> GetConnections();

    /// <summary>Networks in range, strongest first; <paramref name="rescan"/> asks the adapter for a fresh scan.</summary>
    Task<WifiScan> GetNetworksAsync(bool rescan);

    /// <summary>
    /// Joins a network: saved networks use their profile; new ones need <paramref name="password"/>
    /// (personal) or nothing (open). Waits until Windows reports the connection or gives up.
    /// </summary>
    Task<WifiConnectResult> ConnectAsync(WifiNetwork network, string? password);

    Task DisconnectAsync();
}

public enum PowerMode
{
    Efficiency,
    Balanced,
    Performance,
}

/// <param name="Remaining">Estimated time on battery; null while charging or unknown.</param>
public sealed record BatteryDetails(bool HasBattery, int Percent, bool IsCharging, bool PluggedIn, TimeSpan? Remaining, bool SaverOn);

/// <summary>Battery details and the Windows power mode (efficiency, balanced, performance).</summary>
public interface IPowerService
{
    BatteryDetails GetBattery();

    /// <summary>Null when the active power plan has no power modes (a custom plan).</summary>
    PowerMode? GetMode();

    bool SetMode(PowerMode mode);
}

public sealed record NetworkTotals(long BytesReceived, long BytesSent, string AdapterNames);

public sealed record DriveUsage(string Name, string Label, ulong FreeBytes, ulong TotalBytes, bool IsSystem);

public sealed record RecycleBinState(long Items, long Bytes);

/// <summary>Network counters, drives and the Recycle Bin.</summary>
public interface IStorageNetworkMonitor
{
    NetworkTotals GetNetworkTotals();

    IReadOnlyList<DriveUsage> GetDrives();

    RecycleBinState GetRecycleBin();

    void OpenRecycleBin();

    /// <summary>Empties the bin. Windows shows its own confirmation first.</summary>
    void EmptyRecycleBin();

    void OpenThisPc();
}

public sealed record KeyboardLayoutInfo(string Short, string Name);

public interface IKeyboardLayoutService
{
    /// <summary>Layout of the window in front (the one the user types into).</summary>
    KeyboardLayoutInfo GetCurrent();

    /// <summary>Asks the window in front to switch to the next installed layout.</summary>
    void SwitchNext();
}

public enum RadioKind
{
    WiFi,
    Bluetooth,
}

public sealed record RadioInfo(RadioKind Kind, string Name, bool IsOn);

public interface IRadioService
{
    Task<IReadOnlyList<RadioInfo>> GetRadiosAsync();

    Task<bool> SetAsync(RadioKind kind, bool on);
}

public sealed record DisplayBrightness(string Id, string Name, int Percent);

/// <summary>Brightness for laptop panels (WMI) and external monitors (DDC/CI).</summary>
public interface IBrightnessService
{
    /// <summary>Slow (DDC/CI round trips); call off the UI thread.</summary>
    IReadOnlyList<DisplayBrightness> GetDisplays();

    void Set(string id, int percent);
}

/// <summary>Application-level commands used by views and the tray.</summary>
public interface IAppShell
{
    void ShowDock();

    void HideDock();

    /// <param name="tab">Settings tab to open ("calendar"); null keeps the last one.</param>
    void ShowSettings(string? tab = null);

    /// <summary>Opens (or closes) the launcher: search apps, files and quick answers.</summary>
    void ToggleLauncher();

    /// <summary>Opens (or closes) the clipboard history window.</summary>
    void ToggleClipboard();

    /// <summary>Closes NexaDock cleanly and starts it again (after a restore or an update).</summary>
    void Restart();

    /// <param name="display">The secondary display whose bar to edit; null for the main bar.</param>
    void ShowWidgetPicker(string? display = null);

    /// <summary>Non-blocking user notification (tray balloon) for recoverable failures.</summary>
    void NotifyError(string title, string message);

    /// <summary>Non-blocking informational notification (e.g. a finished timer).</summary>
    void Notify(string title, string message);

    void Exit();
}
