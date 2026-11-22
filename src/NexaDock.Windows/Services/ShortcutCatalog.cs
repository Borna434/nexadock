using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>First-run shortcuts limited to apps that actually exist on this machine.</summary>
public sealed class ShortcutCatalog : IShortcutCatalog
{
    private static Guid _imageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public IReadOnlyList<DockShortcut> GetDefaultShortcuts()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new (string Id, string Name, string[] Targets)[]
        {
            ("explorer", "File Explorer", [Path.Combine(windows, "explorer.exe")]),
            ("edge", "Microsoft Edge", [Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe")]),
            ("chrome", "Google Chrome", [Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe")]),
            ("firefox", "Firefox", [Path.Combine(pf, @"Mozilla Firefox\firefox.exe")]),
            ("terminal", "Terminal", [@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"]),
            ("notepad", "Notepad", [@"shell:AppsFolder\Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", Path.Combine(system, "notepad.exe")]),
            ("calculator", "Calculator", [@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"]),
            ("paint", "Paint", [@"shell:AppsFolder\Microsoft.Paint_8wekyb3d8bbwe!App", Path.Combine(system, "mspaint.exe")]),
            ("settings", "Windows Settings", [@"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"]),
            ("taskmgr", "Task Manager", [Path.Combine(system, "Taskmgr.exe")]),
        };

        var result = new List<DockShortcut>();
        foreach (var (id, name, targets) in candidates)
        {
            foreach (var target in targets)
            {
                if (Exists(target))
                {
                    result.Add(new DockShortcut($"default-{id}", name, target));
                    break;
                }
            }
        }

        return result;
    }

    public DockShortcut CreateFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = Path.GetFileNameWithoutExtension(path);

        if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    name = description.Trim();
                }
            }
            catch (FileNotFoundException)
            {
            }
        }

        return DockShortcut.Create(string.IsNullOrWhiteSpace(name) ? "Shortcut" : name, path);
    }

    private static bool Exists(string target)
    {
        if (!target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(target);
        }

        try
        {
            var hr = SHCreateItemFromParsingName(target, IntPtr.Zero, ref _imageFactoryIid, out var item);
            if (item is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(item);
            }

            return hr >= 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
