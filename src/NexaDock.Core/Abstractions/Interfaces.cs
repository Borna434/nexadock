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

    void Start();

    void Activate(IntPtr hwnd);

    void Minimize(IntPtr hwnd);

    void Close(IntPtr hwnd);

    /// <summary>True when the foreground window covers the whole given monitor (game, video, slideshow).</summary>
    bool IsForegroundFullscreen(MonitorInfo monitor);
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
    bool CanNext);

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

/// <summary>Application-level commands used by views and the tray.</summary>
public interface IAppShell
{
    void ShowDock();

    void HideDock();

    void ShowSettings();

    void ShowWidgetPicker();

    /// <summary>Non-blocking user notification (tray balloon) for recoverable failures.</summary>
    void NotifyError(string title, string message);

    void Exit();
}
