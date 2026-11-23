using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Tracks windows that would appear on the Windows taskbar, using WinEvent hooks (with a slow
/// safety poll) so the Apps widget updates as soon as windows open, close or gain focus.
/// </summary>
public sealed class Win32WindowTracker : IWindowTracker, IDisposable
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    private static PROPERTYKEY _aumidKey = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
    private static Guid _propertyStoreIid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    private readonly ILogger<Win32WindowTracker> _logger;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly Dictionary<int, ProcessIdentity> _processCache = new();
    private readonly List<IntPtr> _hooks = new();
    private readonly WinEventDelegate _callback;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _poll;
    private IReadOnlyList<TrackedWindow> _windows = Array.Empty<TrackedWindow>();

    public Win32WindowTracker(ILogger<Win32WindowTracker> logger)
    {
        _logger = logger;
        _callback = OnWinEvent; // keep the delegate alive for the lifetime of the hooks
    }

    public IReadOnlyList<TrackedWindow> Windows => _windows;

    public IntPtr ForegroundWindow { get; private set; }

    public event EventHandler? Changed;

    public void Start()
    {
        if (_debounce is not null)
        {
            return;
        }

        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();

        const uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, flags);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, flags);
        Hook(EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE, flags);
        Hook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE, flags);
        Hook(EventSystemMoveSizeEnd, EventSystemMoveSizeEnd, flags); // a window dragged to another display
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, flags);

        Refresh();
        _logger.LogInformation("Window tracker started with {Count} hooks.", _hooks.Count);
    }

    private void Hook(uint min, uint max, uint flags)
    {
        var h = SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, flags);
        if (h != IntPtr.Zero)
        {
            _hooks.Add(h);
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType != EVENT_SYSTEM_FOREGROUND && (idObject != OBJID_WINDOW || idChild != 0))
        {
            return;
        }

        // Debounce bursts (a window opening fires create/show/name events back to back).
        _debounce?.Stop();
        _debounce?.Start();
    }

    public void Refresh()
    {
        var foreground = GetForegroundWindow();
        var list = new List<TrackedWindow>();
        var livePids = new HashSet<int>();

        EnumWindows((hwnd, _) =>
        {
            if (TryDescribe(hwnd, foreground, out var window))
            {
                list.Add(window);
                livePids.Add(window.ProcessId);
            }

            return true;
        }, IntPtr.Zero);

        foreach (var pid in _processCache.Keys.Where(p => !livePids.Contains(p)).ToList())
        {
            _processCache.Remove(pid);
        }

        var changed = foreground != ForegroundWindow || !list.SequenceEqual(_windows);
        ForegroundWindow = foreground;
        _windows = list;
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool TryDescribe(IntPtr hwnd, IntPtr foreground, out TrackedWindow window)
    {
        window = null!;
        if (!IsWindowVisible(hwnd) || IsCloaked(hwnd))
        {
            return false;
        }

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow && ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0 || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero))
        {
            return false;
        }

        var length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return false;
        }

        var className = GetClass(hwnd);
        if (IgnoredClasses.Contains(className))
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownProcessId)
        {
            return false;
        }

        var title = new StringBuilder(length + 1);
        GetWindowText(hwnd, title, title.Capacity);

        var identity = GetProcessIdentity(pid);
        var aumid = className == "ApplicationFrameWindow" ? GetWindowAumid(hwnd) : identity.PackageAumid;
        if (className == "ApplicationFrameWindow" && aumid is null)
        {
            return false; // empty UWP frame (app still starting or suspended)
        }

        window = new TrackedWindow(hwnd, title.ToString(), pid, identity.ExecutablePath, aumid, IsIconic(hwnd), hwnd == foreground);
        return true;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string GetClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private ProcessIdentity GetProcessIdentity(int pid)
    {
        if (_processCache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        string? path = null;
        string? aumid = null;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var size = 1024;
                var sb = new StringBuilder(size);
                if (QueryFullProcessImageName(handle, 0, sb, ref size))
                {
                    path = sb.ToString();
                }

                var len = 256u;
                var id = new StringBuilder((int)len);
                if (GetApplicationUserModelId(handle, ref len, id) == 0)
                {
                    aumid = id.ToString();
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        var identity = new ProcessIdentity(path, aumid);
        _processCache[pid] = identity;
        return identity;
    }

    private static string? GetWindowAumid(IntPtr hwnd)
    {
        if (SHGetPropertyStoreForWindow(hwnd, ref _propertyStoreIid, out var store) != 0 || store is null)
        {
            return null;
        }

        try
        {
            if (store.GetValue(ref _aumidKey, out var pv) != 0)
            {
                return null;
            }

            try
            {
                return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.pointerValue) : null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint applicationUserModelIdLength, StringBuilder applicationUserModelId);

    public void Activate(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        if (!SetForegroundWindow(hwnd))
        {
            // Foreground lock: a synthetic Alt press grants this process the right to switch.
            var inputs = new[] { Key(VK_MENU, false), Key(VK_MENU, true) };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            SetForegroundWindow(hwnd);
        }

        Refresh();
    }

    public void Minimize(IntPtr hwnd)
    {
        if (IsWindow(hwnd))
        {
            ShowWindow(hwnd, SW_MINIMIZE);
        }
    }

    public void Close(IntPtr hwnd)
    {
        if (IsWindow(hwnd))
        {
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public bool IsForegroundFullscreen(MonitorInfo monitor)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow())
        {
            return false;
        }

        GetWindowThreadProcessId(fg, out var pid);
        if (pid == _ownProcessId)
        {
            return false;
        }

        var cls = GetClass(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
        {
            return false;
        }

        // Maximized windows can report a rectangle covering the whole monitor (frameless apps);
        // they are not full-screen. Real full-screen windows (games, F11, video) are not zoomed.
        if (IsZoomed(fg) || !GetWindowRect(fg, out var r))
        {
            return false;
        }

        var b = monitor.Bounds;
        var coversMonitor = r.Left <= b.X && r.Top <= b.Y && r.Right >= b.Right && r.Bottom >= b.Bottom;
        if (!coversMonitor)
        {
            return false;
        }

        // Windows' own verdict (what the taskbar uses): a window that merely covers the screen,
        // such as a frameless app sized to the monitor, is not "full screen".
        return SHQueryUserNotificationState(out var state) == 0
            && state is QunsBusy or QunsRunningD3DFullScreen or QunsPresentationMode;
    }

    private const int QunsBusy = 2;
    private const int QunsRunningD3DFullScreen = 3;
    private const int QunsPresentationMode = 4;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    internal static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    public void Dispose()
    {
        foreach (var h in _hooks)
        {
            UnhookWinEvent(h);
        }

        _hooks.Clear();
        _poll?.Stop();
        _debounce?.Stop();
    }

    private sealed record ProcessIdentity(string? ExecutablePath, string? PackageAumid);

    private const uint EventSystemMoveSizeEnd = 0x000B;

    public string? GetMonitorDeviceName(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
