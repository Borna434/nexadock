using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using ManagedShell.WindowsTray;
using MsNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using MsRect = ManagedShell.Interop.NativeMethods.Rect;

namespace NexaDock.UI.Services;

/// <summary>One notification-area icon of a background app, as shown on the bar.</summary>
public sealed partial class TrayIconItem : ObservableObject
{
    internal TrayIconItem(MsNotifyIcon icon)
    {
        Source = icon;
        Refresh();
    }

    internal MsNotifyIcon Source { get; }

    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isPinned;

    /// <summary>Stable key used to remember pinned icons (GUID or path + id).</summary>
    public string Identifier => Source.Identifier;

    internal void Refresh()
    {
        Icon = Source.Icon;
        Title = string.IsNullOrWhiteSpace(Source.Title)
            ? Path.GetFileNameWithoutExtension(Source.Path ?? string.Empty)
            : Source.Title.Trim();
        IsPinned = Source.IsPinned;
    }

    // ---- Mouse forwarding (the app reacts exactly as it would on the Windows taskbar) ----------

    /// <summary>Where the icon is on screen (physical pixels), for apps that position flyouts.</summary>
    public void SetPlacement(int left, int top, int right, int bottom) =>
        Source.Placement = new MsRect(left, top, right, bottom);

    public void MouseEnter() => Source.IconMouseEnter(CursorParam());

    public void MouseLeave() => Source.IconMouseLeave(CursorParam());

    public void MouseMove() => Source.IconMouseMove(CursorParam());

    public void MouseDown(MouseButton button) => Source.IconMouseDown(button, CursorParam(), (int)GetDoubleClickTime());

    public void MouseUp(MouseButton button)
    {
        // The app usually brings a window or menu to the front; it may do so because we received
        // the click (our windows never activate themselves).
        if (Source.HWnd != IntPtr.Zero && GetWindowThreadProcessId(Source.HWnd, out var pid) != 0)
        {
            AllowSetForegroundWindow(pid);
        }

        Source.IconMouseUp(button, CursorParam(), (int)GetDoubleClickTime());
    }

    private static uint CursorParam()
    {
        GetCursorPos(out var p);
        return (uint)((p.Y & 0xFFFF) << 16 | (p.X & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}

/// <summary>
/// Hosts the notification area (icons of background apps such as Discord or Steam) through
/// ManagedShell, the library behind RetroBar. Started when a Tray widget is on the bar, stopped
/// when the last one goes or NexaDock exits; Windows keeps its own copy of every icon all along,
/// so nothing is lost if NexaDock stops or crashes.
/// </summary>
public sealed partial class TrayIconHost : ObservableObject, IDisposable
{
    private const string PinnedKey = "tray.pinned";

    private readonly IKeyValueStore _store;
    private readonly ILogger<TrayIconHost> _logger;
    private readonly Dictionary<MsNotifyIcon, TrayIconItem> _items = [];
    private readonly string? _ownPath = Environment.ProcessPath;
    private TrayService? _trayService;
    private NotificationArea? _area;
    private int _users;
    private int _loggedCount = -1;

    public TrayIconHost(IKeyValueStore store, ILogger<TrayIconHost> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Every visible icon, in the order apps registered them.</summary>
    public ObservableCollection<TrayIconItem> All { get; } = [];

    /// <summary>Icons the user pinned to the bar.</summary>
    public ObservableCollection<TrayIconItem> Pinned { get; } = [];

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private bool _hasIcons;

    public void Attach()
    {
        if (++_users > 1 || _area is not null || _startPending)
        {
            return;
        }

        // Start once the bars have settled: while they register their screen space our tray
        // window briefly steps aside, and icons that apps re-add at that moment would go to
        // Explorer instead of us.
        _startPending = true;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _startPending = false;
            if (_users > 0 && _area is null)
            {
                StartHosting();
            }
        };
        timer.Start();
    }

    private bool _startPending;

    private void StartHosting()
    {

        try
        {
            _trayService = new TrayService();
            _area = new NotificationArea(LoadPinned(), _trayService, new ExplorerTrayService());
            _area.Initialize();
            if (_area.IsFailed)
            {
                _logger.LogWarning("Notification area could not be hosted.");
            }

            _area.TrayIcons.CollectionChanged += OnIconsChanged;
            foreach (var icon in _area.TrayIcons)
            {
                Add(icon);
            }

            IsAvailable = !_area.IsFailed;
            Rebuild();
            _logger.LogInformation("Notification area hosted ({Count} icons).", All.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification area could not be started.");
            IsAvailable = false;
            Stop();
        }
    }

    public void Detach()
    {
        if (_users > 0 && --_users == 0)
        {
            Stop();
        }
    }

    /// <summary>Lets apps that ask where the tray is (to place a flyout) find the bar.</summary>
    public void SetHostBounds(int left, int top, int right, int bottom, bool top_edge)
    {
        _area?.SetTrayHostSizeData(new TrayHostSizeData
        {
            edge = top_edge ? ManagedShell.Interop.NativeMethods.ABEdge.ABE_TOP : ManagedShell.Interop.NativeMethods.ABEdge.ABE_BOTTOM,
            rc = new MsRect(left, top, right, bottom),
        });
    }

    public void TogglePin(TrayIconItem item)
    {
        if (item.Source.IsPinned)
        {
            item.Source.Unpin();
        }
        else
        {
            item.Source.Pin();
        }

        if (_area is not null)
        {
            _store.Set(PinnedKey, JsonSerializer.Serialize(_area.PinnedNotifyIcons ?? []));
        }

        item.Refresh();
        Rebuild();
    }

    private void OnIconsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var icon in _items.Keys.ToList())
            {
                Remove(icon);
            }

            foreach (var icon in _area?.TrayIcons ?? [])
            {
                Add(icon);
            }
        }
        else
        {
            foreach (MsNotifyIcon icon in e.OldItems ?? Array.Empty<MsNotifyIcon>())
            {
                Remove(icon);
            }

            foreach (MsNotifyIcon icon in e.NewItems ?? Array.Empty<MsNotifyIcon>())
            {
                Add(icon);
            }
        }

        Rebuild();
    }

    private void Add(MsNotifyIcon icon)
    {
        if (_items.ContainsKey(icon))
        {
            return;
        }

        _items[icon] = new TrayIconItem(icon);
        icon.PropertyChanged += OnIconPropertyChanged;
    }

    private void Remove(MsNotifyIcon icon)
    {
        icon.PropertyChanged -= OnIconPropertyChanged;
        _items.Remove(icon);
    }

    private void OnIconPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MsNotifyIcon icon && _items.TryGetValue(icon, out var item))
        {
            item.Refresh();
            if (e.PropertyName is nameof(MsNotifyIcon.IsHidden) or nameof(MsNotifyIcon.IsPinned) or nameof(MsNotifyIcon.PinOrder))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// Windows' own volume, network and battery icons: the bar has its own controls for these
    /// (the system group), so showing them here too would put them on the bar twice.
    /// </summary>
    private static readonly HashSet<string> DuplicateSystemIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        NotificationArea.VOLUME_GUID,
        NotificationArea.NETWORK_GUID,
        NotificationArea.POWER_GUID,
    };

    /// <summary>Our own tray icon, Windows icons the bar already covers and icons apps asked to hide are left out.</summary>
    private bool IsShown(MsNotifyIcon icon) =>
        !icon.IsHidden
        && !DuplicateSystemIcons.Contains(icon.Identifier ?? string.Empty)
        && !(_ownPath is not null && string.Equals(icon.Path, _ownPath, StringComparison.OrdinalIgnoreCase));

    private void Rebuild()
    {
        var order = _area?.TrayIcons.ToList() ?? [];
        var visible = order.Where(i => _items.ContainsKey(i) && IsShown(i)).Select(i => _items[i]).ToList();
        Sync(All, visible);
        Sync(Pinned, visible.Where(i => i.Source.IsPinned).OrderBy(i => i.Source.PinOrder).ToList());
        HiddenCount = visible.Count - Pinned.Count;
        HasIcons = visible.Count > 0;
        if (visible.Count != _loggedCount)
        {
            _loggedCount = visible.Count;
            _logger.LogInformation("Notification icons: {Count} shown, {Pinned} pinned to the bar.", visible.Count, Pinned.Count);
        }
    }

    private static void Sync(ObservableCollection<TrayIconItem> target, IReadOnlyList<TrayIconItem> wanted)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var current = target.IndexOf(wanted[i]);
            if (current < 0)
            {
                target.Insert(i, wanted[i]);
            }
            else if (current != i)
            {
                target.Move(current, i);
            }
        }
    }

    private string[] LoadPinned()
    {
        try
        {
            var json = _store.Get(PinnedKey);
            return json is null ? NotificationArea.DEFAULT_PINNED : JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return NotificationArea.DEFAULT_PINNED;
        }
    }

    private void Stop()
    {
        if (_area is not null)
        {
            _area.TrayIcons.CollectionChanged -= OnIconsChanged;
        }

        foreach (var icon in _items.Keys.ToList())
        {
            Remove(icon);
        }

        All.Clear();
        Pinned.Clear();
        HiddenCount = 0;
        HasIcons = false;

        try
        {
            _area?.Dispose();
            _trayService?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification area did not stop cleanly.");
        }

        _area = null;
        _trayService = null;
        IsAvailable = false;
    }

    public void Dispose()
    {
        _users = 0;
        Stop();
    }
}
