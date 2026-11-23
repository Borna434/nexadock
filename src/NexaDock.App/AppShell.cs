using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.UI.Localization;
using NexaDock.UI.Views;
using NexaDock.Windows.Services;

namespace NexaDock.App;

/// <summary>Coordinates windows and the tray; the single place that knows about all top-level UI.</summary>
public sealed class AppShell : IAppShell
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AppShell> _logger;
    private SettingsWindow? _settingsWindow;
    private WidgetPickerWindow? _pickerWindow;
    private ImageSource? _appIcon;

    public AppShell(IServiceProvider services, ILogger<AppShell> logger)
    {
        _services = services;
        _logger = logger;
    }

    private DockWindow Dock => _services.GetRequiredService<DockWindow>();

    private BarManager Bars => _services.GetRequiredService<BarManager>();

    public void ShowDock()
    {
        var dock = Dock;
        dock.UserHidden = false;
        if (!dock.IsVisible)
        {
            dock.Show();
        }

        dock.Topmost = false; // re-assert z-order above other topmost windows
        dock.Topmost = true;
        dock.Reposition();
        Bars.ShowAll();
    }

    public void HideDock()
    {
        var dock = Dock;
        dock.UserHidden = true;
        dock.Hide();
        Bars.HideAll();
        Notify(Loc.T("notify.hidden.title"), Loc.T("notify.hidden.body"));
    }

    public void ShowSettings(string? tab = null)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.SelectTab(tab);
            Bring(_settingsWindow);
            return;
        }

        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.SelectTab(tab);
        _settingsWindow.SetAppIcon(AppIcon);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null; // closing settings never closes the bar
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowWidgetPicker(string? display = null)
    {
        if (_pickerWindow is { IsLoaded: true })
        {
            _pickerWindow.SetDisplay(display);
            Bring(_pickerWindow);
            return;
        }

        _pickerWindow = _services.GetRequiredService<WidgetPickerWindow>();
        _pickerWindow.SetDisplay(display);
        _pickerWindow.SetAppIcon(AppIcon);
        _pickerWindow.Closed += (_, _) => _pickerWindow = null;
        _pickerWindow.Show();
        _pickerWindow.Activate();
    }

    private static void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    public void NotifyError(string title, string message) =>
        _services.GetRequiredService<TrayIconService>().ShowNotification(title, message, isError: true);

    public void Notify(string title, string message) =>
        _services.GetRequiredService<TrayIconService>().ShowNotification(title, message, isError: false);

    public void Exit()
    {
        _logger.LogInformation("Exit requested.");
        _settingsWindow?.Close();
        _pickerWindow?.Close();
        Application.Current.Shutdown(0);
    }

    private ImageSource? AppIcon
    {
        get
        {
            if (_appIcon is not null)
            {
                return _appIcon;
            }

            try
            {
                using var stream = TrayIconService.OpenIconStream();
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                _appIcon = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                _appIcon.Freeze();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load application icon.");
            }

            return _appIcon;
        }
    }
}
