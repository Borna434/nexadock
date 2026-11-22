using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>Launches shortcuts through the Windows shell (handles .exe, .lnk, documents, packaged apps and URIs).</summary>
public sealed class ShellAppLauncher : IAppLauncher
{
    private readonly ILogger<ShellAppLauncher> _logger;

    public ShellAppLauncher(ILogger<ShellAppLauncher> logger) => _logger = logger;

    public LaunchResult Launch(DockShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        // The click on our non-activating window gave this process foreground rights; pass them on
        // so the launched app comes to the front instead of flashing in the taskbar.
        AllowSetForegroundWindow(ASFW_ANY);

        var target = Environment.ExpandEnvironmentVariables(shortcut.Target);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = target,
                Arguments = shortcut.Arguments ?? string.Empty,
                UseShellExecute = true,
            };

            var workingDir = shortcut.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(workingDir) && Path.IsPathFullyQualified(target) && File.Exists(target))
            {
                workingDir = Path.GetDirectoryName(target);
            }

            if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
            {
                psi.WorkingDirectory = workingDir;
            }

            using var _ = Process.Start(psi);
            _logger.LogInformation("Launched {Name} ({Target}).", shortcut.Name, shortcut.Target);
            return new LaunchResult(true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user dismissed a UAC prompt. Not an error worth surfacing.
            return new LaunchResult(true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _logger.LogWarning(ex, "Failed to launch {Name} ({Target}).", shortcut.Name, shortcut.Target);
            return new LaunchResult(false, ex.Message);
        }
    }
}
