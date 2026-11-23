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
public sealed class TaskbarController : ITaskbarController
{
    private readonly ILogger<TaskbarController> _logger;

    public TaskbarController(ILogger<TaskbarController> logger) => _logger = logger;

    public int GetState()
    {
        var data = NewData(FindWindow("Shell_TrayWnd", null));
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data).ToUInt32();
    }

    public void Hide()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
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
            ShowWindow(hwnd, SW_HIDE);
        }
    }

    public void Restore(int state)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
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

    private static void SetState(IntPtr tray, int state)
    {
        var data = NewData(tray);
        data.lParam = new IntPtr(state);
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    private static APPBARDATA NewData(IntPtr hwnd) => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    private static IEnumerable<IntPtr> TaskbarWindows()
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
        {
            yield return primary;
        }

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            yield return secondary;
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
