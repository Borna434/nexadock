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
    private IntPtr _appBarHwnd;
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

    public void SetAppBar(IntPtr hwnd, DockEdge edge, PixelRect strip)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = _appBarMessage };
        if (_appBarHwnd != hwnd)
        {
            if (_appBarHwnd != IntPtr.Zero)
            {
                RemoveAppBar(_appBarHwnd);
            }

            SHAppBarMessage(ABM_NEW, ref data);
            _appBarHwnd = hwnd;
            _logger.LogInformation("Registered app bar.");
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
        if (_appBarHwnd == IntPtr.Zero || _appBarHwnd != hwnd)
        {
            return;
        }

        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
        _appBarHwnd = IntPtr.Zero;
        _logger.LogInformation("Removed app bar.");
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

    private static IntPtr PopupWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCACTIVATE:
                // Keep DWM rendering the backdrop in its active (blurred) state.
                handled = true;
                return DefWindowProc(hwnd, msg, new IntPtr(1), lParam);

            case WM_MOUSEACTIVATE:
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
        if (_appBarHwnd != IntPtr.Zero)
        {
            RemoveAppBar(_appBarHwnd);
        }

        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
        _debounce.Stop();
    }
}
