using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.UI.Views;
using NexaDock.Windows.Services;

namespace NexaDock.App;

/// <summary>Coordinates windows and the tray; the single place that knows about all top-level UI.</summary>
public sealed class AppShell : IAppShell
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AppShell> _logger;
    private SettingsWindow? _settingsWindow;
    private ImageSource? _appIcon;

    public AppShell(IServiceProvider services, ILogger<AppShell> logger)
    {
        _services = services;
        _logger = logger;
    }

    private DockWindow Dock => _services.GetRequiredService<DockWindow>();

    public void ShowDock()
    {
        var dock = Dock;
        if (!dock.IsVisible)
        {
            dock.Show();
        }

        dock.Topmost = false; // re-assert z-order above other topmost windows
        dock.Topmost = true;
        dock.Reposition();
    }

    public void HideDock()
    {
        Dock.Hide();
        NotifyInfo("Dock hidden", "Use the NexaDock tray icon › Show Dock to bring it back.");
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.SetAppIcon(AppIcon);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null; // closing settings never closes the dock
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void NotifyError(string title, string message) =>
        _services.GetRequiredService<TrayIconService>().ShowNotification(title, message, isError: true);

    private void NotifyInfo(string title, string message) =>
        _services.GetRequiredService<TrayIconService>().ShowNotification(title, message, isError: false);

    public void Exit()
    {
        _logger.LogInformation("Exit requested.");
        _settingsWindow?.Close();
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
