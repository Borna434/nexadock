using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Hides the Windows taskbar the documented way: sets it to auto-hide (so it frees its screen
/// space) and hides its windows. <see cref="Restore"/> puts back the exact previous state.
/// Nothing on disk outside Windows' own taskbar setting is changed.
/// </summary>
/// <remarks>
/// While notification icons are hosted, NexaDock owns a "Shell_TrayWnd" of its own that sits
/// first in z-order, so only windows of other processes (Explorer's) are touched here. Explorer
/// re-shows its taskbar on some shell events, such as Show desktop or minimizing every window;
/// a WinEvent hook hides it again as soon as it appears.
/// </remarks>
public sealed class TaskbarController : ITaskbarController, IDisposable
{
    private readonly ILogger<TaskbarController> _logger;
    private readonly WinEventDelegate _onShow;
    private IntPtr _showHook;

    public TaskbarController(ILogger<TaskbarController> logger)
    {
        _logger = logger;
        _onShow = OnWindowShown;
    }

    public int GetState()
    {
        using var route = Win32DockWindowHost.RouteAppBarMessagesToWindows();
        var data = NewData(ExplorerTray());
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data).ToUInt32();
    }

    public void Hide()
    {
        var tray = ExplorerTray();
        if (tray == IntPtr.Zero)
        {
            _logger.LogWarning("Taskbar window not found; nothing to hide.");
            return;
        }

        var state = GetState();
        if ((state & ABS_AUTOHIDE) == 0)
        {
            SetState(tray, state | ABS_AUTOHIDE);
        }

        foreach (var hwnd in TaskbarWindows())
        {
            if (IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_HIDE);
            }
        }

        if (_showHook == IntPtr.Zero)
        {
            _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _onShow, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }
    }

    public void Restore(int state)
    {
        Unhook();
        var tray = ExplorerTray();
        if (tray == IntPtr.Zero)
        {
            return;
        }

        SetState(tray, state);
        foreach (var hwnd in TaskbarWindows())
        {
            ShowWindow(hwnd, SW_SHOWNA);
        }

        _logger.LogInformation("Windows taskbar restored (state {State}).", state);
    }

    public void Dispose() => Unhook();

    private void Unhook()
    {
        if (_showHook != IntPtr.Zero)
        {
            UnhookWinEvent(_showHook);
            _showHook = IntPtr.Zero;
        }
    }

    private void OnWindowShown(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
        {
            return;
        }

        var cls = ClassOf(hwnd);
        if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" && !IsOwn(hwnd))
        {
            ShowWindow(hwnd, SW_HIDE);
        }
    }

    private static void SetState(IntPtr tray, int state)
    {
        using var route = Win32DockWindowHost.RouteAppBarMessagesToWindows();
        var data = NewData(tray);
        data.lParam = new IntPtr(state);
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    private static APPBARDATA NewData(IntPtr hwnd) => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    private static bool IsOwn(IntPtr hwnd) =>
        GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid == Environment.ProcessId;

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Explorer's primary taskbar, skipping NexaDock's own tray window.</summary>
    private static IntPtr ExplorerTray()
    {
        var h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            if (!IsOwn(h))
            {
                return h;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<IntPtr> TaskbarWindows()
    {
        var primary = ExplorerTray();
        if (primary != IntPtr.Zero)
        {
            yield return primary;
        }

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (!IsOwn(secondary))
            {
                yield return secondary;
            }
        }
    }
}

/// <summary>Per-user "start with Windows" via the documented HKCU Run key.</summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(NexaDock.Core.ProductInfo.Name) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } path)
        {
            key.SetValue(NexaDock.Core.ProductInfo.Name, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(NexaDock.Core.ProductInfo.Name, throwOnMissingValue: false);
        }
    }
}

/// <summary>Live DWM thumbnails of other windows.</summary>
public sealed class DwmThumbnailService : IThumbnailService
{
    public IntPtr Register(IntPtr hostWindow, IntPtr source, NexaDock.Core.Models.PixelRect destination)
    {
        if (DwmRegisterThumbnail(hostWindow, source, out var thumb) != 0)
        {
            return IntPtr.Zero;
        }

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT { Left = destination.X, Top = destination.Y, Right = destination.Right, Bottom = destination.Bottom },
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(thumb, ref props);
        return thumb;
    }

    public (int Width, int Height) GetSourceSize(IntPtr thumbnail) =>
        DwmQueryThumbnailSourceSize(thumbnail, out var size) == 0 ? (size.cx, size.cy) : (0, 0);

    public void Unregister(IntPtr thumbnail)
    {
        if (thumbnail != IntPtr.Zero)
        {
            DwmUnregisterThumbnail(thumbnail);
        }
    }
}
