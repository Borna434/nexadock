using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Turns WPF windows into bar surfaces: never take activation or keyboard focus, are hidden from
/// Alt+Tab and the taskbar, and render with the active frame state so the acrylic backdrop stays
/// vivid. The main bar is also re-placed whenever displays, DPI or the work area change, and can
/// register as an app bar so maximised windows stop above it.
/// </summary>
public sealed class Win32DockWindowHost : IDockWindowHost, IDisposable
{
    private readonly ILogger<Win32DockWindowHost> _logger;
    private readonly DispatcherTimer _debounce;
    private readonly List<HwndSource> _sources = new();
    private readonly uint _appBarMessage = RegisterWindowMessage("NexaDock.AppBar");
    private readonly HashSet<IntPtr> _appBars = new();
    private bool _listening;

    public Win32DockWindowHost(ILogger<Win32DockWindowHost> logger)
    {
        _logger = logger;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            PlacementInvalidated?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? PlacementInvalidated;

    public void Attach(IntPtr hwnd, bool isMainBar)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle is required.", nameof(hwnd));
        }

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(isMainBar ? MainWndProc : PopupWndProc);
            _sources.Add(source);
        }

        // Draw the frame as active from the start (the window is never actually activated).
        SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);

        if (isMainBar && !_listening)
        {
            _listening = true;
            SystemEvents.DisplaySettingsChanged += OnSystemDisplayChanged;
        }
    }

    public void SetBounds(IntPtr hwnd, PixelRect bounds)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER))
        {
            _logger.LogWarning("SetWindowPos failed for {Bounds}.", bounds);
        }
    }

    /// <summary>Last position sent per app bar: unchanged positions are not sent again.</summary>
    private readonly Dictionary<IntPtr, (DockEdge Edge, PixelRect Strip)> _appBarPositions = new();

    public void SetAppBar(IntPtr hwnd, DockEdge edge, PixelRect strip)
    {
        // Every call briefly steps our tray window aside (see RouteAppBarMessagesToWindows), so
        // only talk to Windows when the reservation really changes.
        if (_appBars.Contains(hwnd) && _appBarPositions.TryGetValue(hwnd, out var last) && last.Edge == edge && last.Strip == strip)
        {
            return;
        }

        _appBarPositions[hwnd] = (edge, strip);
        using var route = RouteAppBarMessagesToWindows();
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = _appBarMessage };
        if (_appBars.Add(hwnd))
        {
            var registered = SHAppBarMessage(ABM_NEW, ref data);
            _logger.LogInformation("Registered app bar ({Count} in total): {Result}.", _appBars.Count, registered);
        }

        var thickness = strip.Height;
        data.uEdge = edge == DockEdge.Top ? ABE_TOP : ABE_BOTTOM;
        data.rc = new RECT { Left = strip.X, Top = strip.Y, Right = strip.Right, Bottom = strip.Bottom };
        SHAppBarMessage(ABM_QUERYPOS, ref data);

        // The system may move the rectangle away from other app bars; keep our thickness.
        if (edge == DockEdge.Top)
        {
            data.rc.Bottom = data.rc.Top + thickness;
        }
        else
        {
            data.rc.Top = data.rc.Bottom - thickness;
        }

        SHAppBarMessage(ABM_SETPOS, ref data);
    }

    public void RemoveAppBar(IntPtr hwnd)
    {
        _appBarPositions.Remove(hwnd);
        if (!_appBars.Remove(hwnd))
        {
            return;
        }

        using var route = RouteAppBarMessagesToWindows();
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
        _logger.LogInformation("Removed app bar.");
    }

    /// <summary>
    /// SHAppBarMessage talks to the first "Shell_TrayWnd" window in z-order. While the
    /// notification-area widget is on, that is our own tray window (it has to be, to receive the
    /// icons), and Windows ignores app bar messages that loop back into the sending process. So
    /// our tray window steps below Explorer's for the duration of the call and is raised again
    /// right after; no icon message can arrive in between because both run on this thread.
    /// </summary>
    private static IDisposable RouteAppBarMessagesToWindows()
    {
        var own = FindOwnTrayWindow();
        if (own == IntPtr.Zero)
        {
            return NoOp.Instance;
        }

        SetWindowPos(own, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOACTIVATE);
        return new Restore(own);
    }

    private static IntPtr FindOwnTrayWindow()
    {
        var me = (uint)Environment.ProcessId;
        var h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            if (GetWindowThreadProcessId(h, out var pid) != 0 && pid == me)
            {
                return h;
            }
        }

        return IntPtr.Zero;
    }

    private sealed class Restore(IntPtr hwnd) : IDisposable
    {
        public void Dispose() =>
            SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOACTIVATE);
    }

    private sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();

        public void Dispose()
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private readonly HashSet<IntPtr> _keyboardWindows = new();

    public void SetKeyboardInput(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = enabled ? ex & ~WS_EX_NOACTIVATE : ex | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        if (enabled)
        {
            _keyboardWindows.Add(hwnd);

            // The click that asked for typing gave this process foreground rights.
            SetForegroundWindow(hwnd);
        }
        else
        {
            _keyboardWindows.Remove(hwnd);
        }
    }

    public PointerState GetPointer()
    {
        GetCursorPos(out var p);
        var down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        return new PointerState(p.X, p.Y, down);
    }

    public IDisposable ObserveMouseDown(Action<PointerState> onDown) => new MouseDownHook(onDown);

    /// <summary>WH_MOUSE_LL hook installed only while a pop-up is open; callbacks run on the UI thread.</summary>
    private sealed class MouseDownHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;

        private readonly Action<PointerState> _onDown;
        private readonly LowLevelMouseProc _proc;
        private IntPtr _hook;

        public MouseDownHook(Action<PointerState> onDown)
        {
            _onDown = onDown;
            _proc = Callback; // keep alive while hooked
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var state = new PointerState(info.pt.X, info.pt.Y, true);

                    // Never do real work inside the hook (it has a strict time budget).
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _onDown(state));
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }

    public PixelRect GetBounds(IntPtr hwnd) =>
        GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : default;

    private IntPtr MainWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBarMessage != 0 && msg == (int)_appBarMessage)
        {
            if (wParam.ToInt32() == ABN_POSCHANGED)
            {
                Invalidate();
            }

            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_DISPLAYCHANGE:
            case WM_DPICHANGED:
                Invalidate();
                break;

            case WM_SETTINGCHANGE when wParam.ToInt64() == SPI_SETWORKAREA:
                Invalidate();
                break;
        }

        return PopupWndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private IntPtr PopupWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCACTIVATE:
                // Keep DWM rendering the backdrop in its active (blurred) state.
                handled = true;
                return DefWindowProc(hwnd, msg, new IntPtr(1), lParam);

            case WM_MOUSEACTIVATE when !_keyboardWindows.Contains(hwnd):
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private void OnSystemDisplayChanged(object? sender, EventArgs e) =>
        _debounce.Dispatcher.BeginInvoke(Invalidate);

    private void Invalidate()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        foreach (var hwnd in new List<IntPtr>(_appBars))
        {
            RemoveAppBar(hwnd);
        }

        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
        _debounce.Stop();
    }
}
