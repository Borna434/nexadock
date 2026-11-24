using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>Shell actions via the same keyboard shortcuts a user would press, or documented URIs.</summary>
public sealed class ShellCommands : IShellCommands
{
    private readonly ILogger<ShellCommands> _logger;

    public ShellCommands(ILogger<ShellCommands> logger) => _logger = logger;

    public void OpenStartMenu() => Chord(VK_LWIN);

    public void OpenQuickSettings() => Chord(VK_LWIN, 'A');

    public void OpenNotificationCenter() => Chord(VK_LWIN, 'N');

    public void OpenSystemUri(string uri) => Start(uri);

    public void OpenNetworkFlyout() => Start("ms-availablenetworks:");

    public void OpenSoundFlyout() => Chord(VK_LWIN, 0x11 /* VK_CONTROL */, (ushort)'V');

    public void ToggleDesktop() => Chord(VK_LWIN, 'D');

    public void OpenTaskManager() => Start("taskmgr.exe");

    public void OpenDateTimeSettings() => Start("ms-settings:dateandtime");

    private void Start(string target)
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not start {Target}.", target);
        }
    }

    public void PasteInto(IntPtr window)
    {
        if (window != IntPtr.Zero)
        {
            SetForegroundWindow(window);
        }

        // Give the target a moment to take focus before the keys arrive.
        System.Threading.Thread.Sleep(80);
        Chord(0x11 /* VK_CONTROL */, (ushort)'V');
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>Presses the keys in order, then releases them in reverse order.</summary>
    private void Chord(params ushort[] keys)
    {
        var inputs = new INPUT[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            inputs[i] = Win32WindowTracker.Key(keys[i], up: false);
            inputs[keys.Length * 2 - 1 - i] = Win32WindowTracker.Key(keys[i], up: true);
        }

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            _logger.LogWarning("SendInput was blocked (error {Error}).", Marshal.GetLastWin32Error());
        }
    }

    private void Chord(ushort modifier, char key) => Chord(modifier, (ushort)char.ToUpperInvariant(key));
}
