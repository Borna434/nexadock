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

/// <summary>Enumerates displays.</summary>
public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}

/// <summary>Computes where the dock window goes.</summary>
public interface IDockPositioner
{
    /// <param name="requiredContentWidth">Minimum glass width (DIPs) needed to show all content.</param>
    /// <param name="windowPadding">Transparent margin (DIPs) around the glass, e.g. for a shadow.</param>
    PixelRect Calculate(MonitorInfo monitor, DockSettings settings, double requiredContentWidth, double windowPadding = 0);
}

public sealed record BackdropCapabilities(bool SupportsSystemBackdrop, int OsBuild, string Description);

public sealed record BackdropOptions(bool DarkMode);

/// <summary>Native backdrop support (DWM system backdrop on Windows 11 22H2+).</summary>
public interface IBackdropService
{
    BackdropCapabilities Capabilities { get; }

    /// <summary>The kind the dock should be created with (decided before the window exists).</summary>
    BackdropKind PreferredKind { get; }

    /// <summary>Applies the native backdrop. Returns false when DWM rejected it.</summary>
    bool TryApply(IntPtr hwnd, BackdropOptions options);

    void SetDarkMode(IntPtr hwnd, bool dark);
}

/// <summary>Native window behaviour for the dock: non-activating tool window, placement, change notifications.</summary>
public interface IDockWindowHost
{
    /// <summary>Makes the window non-activating, hidden from Alt+Tab, and starts listening for display changes.</summary>
    void Attach(IntPtr hwnd);

    void SetBounds(IntPtr hwnd, PixelRect bounds);

    /// <summary>Raised (on the UI thread) when displays, DPI or the work area changed.</summary>
    event EventHandler? PlacementInvalidated;
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
}

/// <summary>Application-level commands used by views and the tray.</summary>
public interface IAppShell
{
    void ShowDock();

    void HideDock();

    void ShowSettings();

    /// <summary>Non-blocking user notification (tray balloon) for recoverable failures.</summary>
    void NotifyError(string title, string message);

    void Exit();
}
