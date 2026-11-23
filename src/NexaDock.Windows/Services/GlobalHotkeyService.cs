using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;

namespace NexaDock.Windows.Services;

/// <summary>
/// One system-wide shortcut (RegisterHotKey) on a hidden message window. Registration fails when
/// another app already owns the combination (PowerToys Run uses Alt+Space, for example).
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkey, IDisposable
{
    private const int Id = 0x4E44; // "ND"
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;

    private readonly ILogger<GlobalHotkeyService> _logger;
    private HwndSource? _window;
    private bool _registered;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger) => _logger = logger;

    public event EventHandler? Pressed;

    public event EventHandler? ClipboardPressed;

    private const int ClipboardId = 0x4E45;
    private const uint VK_V = 0x56;
    private bool _clipboardRegistered;

    public bool RegisterClipboard(bool enabled)
    {
        _window ??= CreateWindow();
        if (_clipboardRegistered)
        {
            UnregisterHotKey(_window.Handle, ClipboardId);
            _clipboardRegistered = false;
        }

        if (!enabled)
        {
            return true;
        }

        _clipboardRegistered = RegisterHotKey(_window.Handle, ClipboardId, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_V);
        if (!_clipboardRegistered)
        {
            _logger.LogWarning("Clipboard shortcut Win+Alt+V is taken by another app (error {Error}).", Marshal.GetLastWin32Error());
        }

        return _clipboardRegistered;
    }

    public bool IsRegistered => _registered;

    public bool Register(LauncherHotkey hotkey)
    {
        Unregister();
        if (hotkey == LauncherHotkey.Off)
        {
            return true;
        }

        _window ??= CreateWindow();
        var modifiers = hotkey switch
        {
            LauncherHotkey.AltSpace => MOD_ALT,
            LauncherHotkey.CtrlAltSpace => MOD_CONTROL | MOD_ALT,
            LauncherHotkey.WinShiftSpace => MOD_WIN | MOD_SHIFT,
            _ => MOD_ALT,
        };

        _registered = RegisterHotKey(_window.Handle, Id, modifiers | MOD_NOREPEAT, VK_SPACE);
        if (_registered)
        {
            _logger.LogInformation("Launcher shortcut registered: {Hotkey}.", hotkey);
        }
        else
        {
            _logger.LogWarning("Launcher shortcut {Hotkey} is taken by another app (error {Error}).", hotkey, Marshal.GetLastWin32Error());
        }

        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _window is not null)
        {
            UnregisterHotKey(_window.Handle, Id);
            _registered = false;
        }
    }

    private HwndSource CreateWindow()
    {
        var parameters = new HwndSourceParameters("NexaDock.Hotkeys") { WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
        var source = new HwndSource(parameters);
        source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
            {
                handled = true;
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == ClipboardId)
            {
                handled = true;
                ClipboardPressed?.Invoke(this, EventArgs.Empty);
            }

            return IntPtr.Zero;
        });
        return source;
    }

    public void Dispose()
    {
        Unregister();
        _window?.Dispose();
        _window = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
