using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NexaDock.Core;
using NexaDock.Core.Abstractions;

namespace NexaDock.Windows.Services;

/// <summary>Notification-area icon with Show Dock, Settings and Exit.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly IAppShell _shell;
    private NotifyIcon? _icon;

    public TrayIconService(IAppShell shell) => _shell = shell;

    public void Show()
    {
        if (_icon is not null)
        {
            return;
        }

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        _showItem = (ToolStripMenuItem)menu.Items.Add("Show Dock", null, (_, _) => _shell.ShowDock());
        _settingsItem = (ToolStripMenuItem)menu.Items.Add("Settings…", null, (_, _) => _shell.ShowSettings());
        _widgetsItem = (ToolStripMenuItem)menu.Items.Add("Widgets…", null, (_, _) => _shell.ShowWidgetPicker());
        menu.Items.Add(new ToolStripSeparator());
        _exitItem = (ToolStripMenuItem)menu.Items.Add($"Exit {ProductInfo.Name}", null, (_, _) => _shell.Exit());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(SystemInformation.SmallIconSize),
            Text = $"{ProductInfo.Name} — {ProductInfo.Tagline}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _shell.ShowDock();
            }
        };
        _icon.DoubleClick += (_, _) => _shell.ShowSettings();
    }

    private ToolStripMenuItem? _showItem;
    private ToolStripMenuItem? _settingsItem;
    private ToolStripMenuItem? _widgetsItem;
    private ToolStripMenuItem? _exitItem;

    /// <summary>Localized menu text (the tray lives in the Windows layer, so text is passed in).</summary>
    public void SetTexts(string show, string settings, string widgets, string exit, string tooltip)
    {
        if (_showItem is null || _icon is null)
        {
            return;
        }

        _showItem.Text = show;
        _settingsItem!.Text = settings;
        _widgetsItem!.Text = widgets;
        _exitItem!.Text = exit;
        _icon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    /// <summary>Shows a non-blocking balloon / toast from the tray icon.</summary>
    public void ShowNotification(string title, string message, bool isError)
    {
        if (_icon is null)
        {
            return;
        }

        _icon.ShowBalloonTip(5000, title, message, isError ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    /// <summary>Loads the embedded application icon at the requested size.</summary>
    public static Icon LoadIcon(Size size)
    {
        using var stream = typeof(TrayIconService).Assembly.GetManifestResourceStream("NexaDock.ico")
            ?? throw new InvalidOperationException("Embedded icon NexaDock.ico is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return new Icon(copy, size);
    }

    /// <summary>The raw .ico bytes, for WPF window icons.</summary>
    public static Stream OpenIconStream() =>
        typeof(TrayIconService).Assembly.GetManifestResourceStream("NexaDock.ico")
        ?? throw new InvalidOperationException("Embedded icon NexaDock.ico is missing.");

    public void Dispose()
    {
        if (_icon is null)
        {
            return;
        }

        // Hide before disposing so no ghost icon stays in the notification area.
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _icon = null;
    }
}
