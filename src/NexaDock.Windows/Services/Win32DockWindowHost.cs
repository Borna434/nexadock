using System;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Turns a WPF window into a dock surface: never takes activation/keyboard focus, hidden from
/// Alt+Tab and the taskbar, rendered with the active frame state so the acrylic backdrop stays
/// vivid, and re-placed whenever displays, DPI or the work area change.
/// </summary>
public sealed class Win32DockWindowHost : IDockWindowHost, IDisposable
{
    private readonly ILogger<Win32DockWindowHost> _logger;
    private readonly DispatcherTimer _debounce;
    private HwndSource? _source;

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

    public void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle is required.", nameof(hwnd));
        }

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        // Draw the frame as active from the start (the window will never actually be activated).
        SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);

        SystemEvents.DisplaySettingsChanged += OnSystemDisplayChanged;
        _logger.LogInformation("Dock window attached (non-activating tool window).");
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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCACTIVATE:
                // Keep DWM rendering the backdrop in its active (blurred) state instead of the
                // flat inactive fallback colour.
                handled = true;
                return DefWindowProc(hwnd, msg, new IntPtr(1), lParam);

            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

            case WM_DISPLAYCHANGE:
            case WM_DPICHANGED:
                Invalidate();
                break;

            case WM_SETTINGCHANGE when wParam.ToInt64() == SPI_SETWORKAREA:
                Invalidate();
                break;
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
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
        _source?.RemoveHook(WndProc);
        _debounce.Stop();
    }
}
