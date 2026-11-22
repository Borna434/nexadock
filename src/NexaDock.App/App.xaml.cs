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
using NexaDock.UI.ViewModels;
using NexaDock.UI.Views;
using NexaDock.Windows.Services;

namespace NexaDock.App;

/// <summary>Composition root and process lifetime.</summary>
public partial class App : Application
{
    private IHost? _host;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showSignalRegistration;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, ProductInfo.SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            // Another instance is running: ask it to show its dock, then leave quietly.
            if (EventWaitHandle.TryOpenExisting(ProductInfo.ShowDockEventName, out var existing))
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
        builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogPath));
        builder.Logging.SetMinimumLevel(LogLevel.Information);

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
        services.AddSingleton<AppShell>();
        services.AddSingleton<IAppShell>(sp => sp.GetRequiredService<AppShell>());
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<DockViewModel>();
        services.AddSingleton<DockWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        _host = builder.Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled exception.");

        _host.Start();
        _logger.LogInformation("{Product} starting on Windows build {Build}.", ProductInfo.DisplayTitle, Environment.OSVersion.Version.Build);

        InitializeSettings(_host.Services);

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

        if (settings.LoadStatus == SettingsLoadStatus.Recovered)
        {
            services.GetRequiredService<AppShell>().NotifyError(
                $"{ProductInfo.Name} settings were reset",
                "The stored settings could not be read, so defaults were restored. The old data was kept next to the database.");
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

            if (_host is not null)
            {
                _host.Services.GetRequiredService<TrayIconService>().Dispose();
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
