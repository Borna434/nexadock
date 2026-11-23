using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexaDock.Core;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Settings;
using NexaDock.Infrastructure;
using NexaDock.Infrastructure.Logging;
using NexaDock.Infrastructure.Storage;
using NexaDock.Core.Widgets;
using NexaDock.UI.ViewModels;
using NexaDock.UI.Views;
using NexaDock.UI.Widgets;
using NexaDock.Windows.Services;

namespace NexaDock.App;

/// <summary>Composition root and process lifetime.</summary>
public partial class App : Application
{
    private IHost? _host;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showSignalRegistration;
    private EventWaitHandle? _settingsSignal;
    private RegisteredWaitHandle? _settingsSignalRegistration;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, ProductInfo.SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            // Another instance is running: ask it to show its bar (or Settings), then leave quietly.
            var eventName = e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)
                ? ProductInfo.ShowSettingsEventName
                : ProductInfo.ShowDockEventName;
            if (EventWaitHandle.TryOpenExisting(eventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        var builder = Host.CreateApplicationBuilder(e.Args);
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        var logLevel = string.Equals(Environment.GetEnvironmentVariable("NEXADOCK_LOG"), "debug", StringComparison.OrdinalIgnoreCase) ? LogLevel.Debug : LogLevel.Information;
        builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogPath, logLevel));
        builder.Logging.SetMinimumLevel(logLevel);

        var services = builder.Services;
        services.AddSingleton(new AppDataLocation(AppPaths.DataDirectory));
        services.AddSingleton<ISettingsStore>(sp => new SqliteSettingsStore(AppPaths.DatabasePath, sp.GetRequiredService<ILogger<SqliteSettingsStore>>()));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<IDockPositioner, DockPositionCalculator>();
        services.AddSingleton<IMonitorProvider, Win32MonitorProvider>();
        services.AddSingleton<IBackdropService, DwmBackdropService>();
        services.AddSingleton<IDockWindowHost, Win32DockWindowHost>();
        services.AddSingleton<IIconProvider, ShellIconProvider>();
        services.AddSingleton<IAppLauncher, ShellAppLauncher>();
        services.AddSingleton<IShortcutCatalog, ShortcutCatalog>();
        services.AddSingleton<IWindowTracker, Win32WindowTracker>();
        services.AddSingleton<IShellCommands, ShellCommands>();
        services.AddSingleton<ISystemMonitor, SystemMonitor>();
        services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
        services.AddSingleton<IMediaService, WinRtMediaService>();
        services.AddSingleton<IThumbnailService, DwmThumbnailService>();
        services.AddSingleton<ITaskbarController, TaskbarController>();
        services.AddSingleton<IStartupRegistration, RegistryStartupRegistration>();
        services.AddSingleton<TaskbarCoordinator>();
        services.AddSingleton<AppShell>();
        services.AddSingleton<IAppShell>(sp => sp.GetRequiredService<AppShell>());
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<WidgetFactory>();
        services.AddSingleton<DockViewModel>();
        services.AddSingleton<DockWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<WidgetPickerViewModel>();
        services.AddTransient<WidgetPickerWindow>();

        _host = builder.Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled exception.");
            RestoreTaskbar(); // never leave the user without a taskbar
        };

        _host.Start();
        _logger.LogInformation("{Product} starting on Windows build {Build}.", ProductInfo.DisplayTitle, Environment.OSVersion.Version.Build);

        InitializeSettings(_host.Services);
        _host.Services.GetRequiredService<TaskbarCoordinator>().Start();

        var shell = _host.Services.GetRequiredService<AppShell>();
        shell.ShowDock();
        _host.Services.GetRequiredService<TrayIconService>().Show();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ProductInfo.ShowDockEventName);
        _showSignalRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showSignal,
            (_, _) => Dispatcher.BeginInvoke(shell.ShowDock),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ProductInfo.ShowSettingsEventName);
        _settingsSignalRegistration = ThreadPool.RegisterWaitForSingleObject(
            _settingsSignal,
            (_, _) => Dispatcher.BeginInvoke(shell.ShowSettings),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            shell.ShowSettings();
        }
    }

    private void InitializeSettings(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsService>();
        settings.Load();
        settings.SaveFailed += (_, ex) => _logger?.LogError(ex, "Saving settings failed.");
        _logger?.LogInformation("Settings load status: {Status}.", settings.LoadStatus);

        if (!settings.Current.ShortcutsInitialized)
        {
            var defaults = services.GetRequiredService<IShortcutCatalog>().GetDefaultShortcuts();
            settings.Update(s => s with
            {
                Shortcuts = defaults,
                ShortcutsInitialized = true,
                ReducedMotion = s.ReducedMotion || !SystemParameters.ClientAreaAnimation,
            });
            settings.Flush();
            _logger?.LogInformation("Seeded {Count} default shortcuts.", defaults.Count);
        }

        if (!settings.Current.WidgetsInitialized)
        {
            var uses24 = !System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
            settings.Update(s => s with
            {
                Widgets = WidgetCatalog.DefaultLayout(),
                WidgetsInitialized = true,
                Use24HourClock = uses24,
            });
            settings.Flush();
            _logger?.LogInformation("Seeded the default widget layout.");
        }

        // The Run key is the source of truth (the user may disable startup in Task Manager).
        var startup = services.GetRequiredService<IStartupRegistration>().IsEnabled;
        if (startup != settings.Current.LaunchAtStartup)
        {
            settings.Update(s => s with { LaunchAtStartup = startup });
        }

        if (settings.LoadStatus == SettingsLoadStatus.Recovered)
        {
            services.GetRequiredService<AppShell>().NotifyError(
                $"{ProductInfo.Name} settings were reset",
                "The stored settings could not be read, so defaults were restored. The old data was kept next to the database.");
        }
    }

    private void RestoreTaskbar()
    {
        try
        {
            _host?.Services.GetRequiredService<TaskbarCoordinator>().RestoreIfNeeded();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Restoring the Windows taskbar failed.");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception.");
        e.Handled = true; // keep the dock alive; the error is logged
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _showSignalRegistration?.Unregister(null);
            _showSignal?.Dispose();
            _settingsSignalRegistration?.Unregister(null);
            _settingsSignal?.Dispose();

            if (_host is not null)
            {
                _host.Services.GetRequiredService<TrayIconService>().Dispose();
                RestoreTaskbar();
                _host.Services.GetRequiredService<DockWindow>().Close(); // removes the app bar reservation
                _host.Services.GetRequiredService<SettingsService>().Flush();
                _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                _host.Dispose(); // disposes singletons: settings (final flush), window host, view model
                _logger?.LogInformation("{Product} exited cleanly.", ProductInfo.Name);
            }
        }
        finally
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
