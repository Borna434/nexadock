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
using NexaDock.UI.Localization;
using NexaDock.UI.Services;
using NexaDock.UI.ViewModels;
using NexaDock.UI.Views;
using NexaDock.UI.Widgets;
using NexaDock.Windows.Services;

namespace NexaDock.App;

/// <summary>Composition root and process lifetime.</summary>
public partial class App : Application
{
    private const string CenteredLayoutKey = "layout.centered.v1";
    private const string TrayAddedKey = "layout.tray.v1";
    private const string ClipboardAddedKey = "layout.clipboard.v1";

    private IHost? _host;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showSignalRegistration;
    private EventWaitHandle? _settingsSignal;
    private RegisteredWaitHandle? _settingsSignalRegistration;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitSignalRegistration;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Restart (after a restore or an update): wait until the previous instance has gone.
        var waitIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--wait-for", StringComparison.OrdinalIgnoreCase));
        if (waitIndex >= 0 && waitIndex + 1 < e.Args.Length && int.TryParse(e.Args[waitIndex + 1], out var previousPid))
        {
            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(previousPid);
                previous.WaitForExit(20_000);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }

        _singleInstance = new Mutex(initiallyOwned: true, ProductInfo.SingleInstanceMutexName, out var isFirst);
        if (isFirst && e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            // Nothing is running; "--exit" must not start the bar.
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        if (!isFirst)
        {
            // Another instance is running: ask it to show its bar (or Settings), then leave quietly.
            var eventName = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase)
                ? ProductInfo.ExitEventName
                : e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)
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
        services.AddSingleton(sp => new SqliteSettingsStore(AppPaths.DatabasePath, sp.GetRequiredService<ILogger<SqliteSettingsStore>>()));
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SqliteSettingsStore>());
        services.AddSingleton<IKeyValueStore>(sp => sp.GetRequiredService<SqliteSettingsStore>());
        services.AddSingleton<IAudioService, CoreAudioService>();
        services.AddSingleton<IStorageNetworkMonitor, StorageNetworkMonitor>();
        services.AddSingleton<IKeyboardLayoutService, KeyboardLayoutService>();
        services.AddSingleton<IRadioService, WinRtRadioService>();
        services.AddSingleton<IWifiService, WifiService>();
        services.AddSingleton<IPowerService, PowerService>();
        services.AddSingleton<IBrightnessService, BrightnessService>();
        services.AddSingleton<PulseCenter>();
        services.AddSingleton<WallpaperAccent>();
        services.AddSingleton<NexaDock.Core.Calendar.ICalendarSource, NexaDock.Infrastructure.Calendar.IcsCalendarSource>();
        services.AddSingleton<NexaDock.Core.Calendar.IGoogleCalendarService, NexaDock.Infrastructure.Calendar.GoogleCalendarClient>();
        services.AddSingleton<CalendarFeed>();
        services.AddSingleton<NexaDock.Core.Launcher.IAppIndex, ShellAppIndex>();
        services.AddSingleton<NexaDock.Core.Launcher.IFileSearch, WindowsFileSearch>();
        services.AddSingleton<IGlobalHotkey, GlobalHotkeyService>();
        services.AddSingleton<IClipboardMonitor, ClipboardMonitor>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<LauncherWindow>();
        services.AddSingleton<NexaDock.Core.Updates.IUpdateSource, NexaDock.Infrastructure.Updates.GitHubUpdateSource>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<TrayIconHost>();
        services.AddSingleton<NexaDock.Core.Usage.IClaudeUsageSource, NexaDock.Infrastructure.Usage.ClaudeLogReader>();
        services.AddSingleton<NexaDock.Core.Usage.IClaudeLimitsSource, NexaDock.Infrastructure.Usage.ClaudeLimitsClient>();
        services.AddSingleton<ProfileCoordinator>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<IDockPositioner, DockPositionCalculator>();
        services.AddSingleton<IMonitorProvider, Win32MonitorProvider>();
        services.AddSingleton<IBackdropService, DwmBackdropService>();
        services.AddSingleton<IDockWindowHost, Win32DockWindowHost>();
        services.AddTransient<IScreenStripCapture, ScreenStripCapture>();
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
        services.AddSingleton(BarTarget.Main);
        services.AddSingleton<BarManager>();
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
        var settingsService = _host.Services.GetRequiredService<SettingsService>();
        Loc.Apply(settingsService.Current.Language);
        settingsService.Changed += (_, args) =>
        {
            if (args.Previous.Language != args.Current.Language && Loc.Apply(args.Current.Language))
            {
                ApplyTrayTexts();
                Dispatcher.BeginInvoke(() => LocalizeDefaultProfile(settingsService));
            }
        };

        var wallpaper = _host.Services.GetRequiredService<WallpaperAccent>();
        wallpaper.SetEnabled(settingsService.Current.AccentFromWallpaper);
        settingsService.Changed += (_, a) => wallpaper.SetEnabled(a.Current.AccentFromWallpaper);

        _host.Services.GetRequiredService<TaskbarCoordinator>().Start();
        _host.Services.GetRequiredService<PulseCenter>().Start();

        var shell = _host.Services.GetRequiredService<AppShell>();

        // Listen for --exit, --settings and "show" first: even if something below fails, the
        // installer can still close this instance cleanly (and restore the taskbar).
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
            (_, _) => Dispatcher.BeginInvoke(() => shell.ShowSettings()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // `NexaDock.exe --exit` (used by the installer) closes this instance cleanly.
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ProductInfo.ExitEventName);
        _exitSignalRegistration = ThreadPool.RegisterWaitForSingleObject(
            _exitSignal,
            (_, _) => Dispatcher.BeginInvoke(shell.Exit),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        shell.ShowDock();
        _host.Services.GetRequiredService<BarManager>().Start();
        _host.Services.GetRequiredService<UpdateService>().Start();
        StartLauncherShortcut(settingsService, shell);
        _host.Services.GetRequiredService<TrayIconService>().Show();
        ApplyTrayTexts();
        _host.Services.GetRequiredService<ProfileCoordinator>().Start();

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            shell.ShowSettings();
        }
    }

    private void InitializeSettings(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsService>();
        settings.Load();
        Loc.Apply(settings.Current.Language);
        LocalizeDefaultProfile(settings);
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

        // One-time move to the centred layout (Phase 4): every profile gets the logical order.
        var store = services.GetRequiredService<IKeyValueStore>();
        if (store.Get(CenteredLayoutKey) is null)
        {
            settings.Update(s => s with
            {
                Widgets = WidgetArranger.Arrange(s.Widgets),
                Profiles = s.Profiles.Select(p => p with { Widgets = WidgetArranger.Arrange(p.Widgets) }).ToList(),
            });
            settings.Flush();
            store.Set(CenteredLayoutKey, "1");
            _logger?.LogInformation("Arranged widgets around the centred apps.");
        }

        // One-time: put the new notification icons widget on bars that have the apps (Phase 4).
        if (store.Get(TrayAddedKey) is null)
        {
            static IReadOnlyList<Core.Models.WidgetPlacement> WithTray(IReadOnlyList<Core.Models.WidgetPlacement> widgets)
            {
                if (widgets.Any(w => w.TypeId == WidgetTypes.Tray) || !widgets.Any(w => w.TypeId == WidgetTypes.Apps))
                {
                    return widgets;
                }

                var list = widgets.ToList();
                list.Insert(WidgetArranger.InsertIndex(list, WidgetTypes.Tray), Core.Models.WidgetPlacement.Create(WidgetTypes.Tray));
                return list;
            }

            settings.Update(s => s with
            {
                Widgets = WithTray(s.Widgets),
                Profiles = s.Profiles.Select(p => p with { Widgets = WithTray(p.Widgets) }).ToList(),
            });
            settings.Flush();
            store.Set(TrayAddedKey, "1");
            _logger?.LogInformation("Added the notification icons widget.");
        }

        if (store.Get(ClipboardAddedKey) is null)
        {
            static IReadOnlyList<Core.Models.WidgetPlacement> WithClipboard(IReadOnlyList<Core.Models.WidgetPlacement> widgets)
            {
                if (widgets.Any(w => w.TypeId == WidgetTypes.Clipboard) || !widgets.Any(w => w.TypeId == WidgetTypes.Apps))
                {
                    return widgets;
                }

                var list = widgets.ToList();
                list.Insert(WidgetArranger.InsertIndex(list, WidgetTypes.Clipboard), Core.Models.WidgetPlacement.Create(WidgetTypes.Clipboard));
                return list;
            }

            settings.Update(s => s with
            {
                Widgets = WithClipboard(s.Widgets),
                Profiles = s.Profiles.Select(p => p with { Widgets = WithClipboard(p.Widgets) }).ToList(),
            });
            settings.Flush();
            store.Set(ClipboardAddedKey, "1");
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
                Loc.F("notify.reset.title", ProductInfo.Name),
                Loc.T("notify.reset.body"));
        }
    }

    /// <summary>The built-in default profile carries a placeholder name; show it in the UI language.</summary>
    private static void LocalizeDefaultProfile(ISettingsService settings)
    {
        var builtInNames = new[] { "Everyday", "Svakodnevno" };
        var wanted = Loc.T("profiles.default");
        settings.Update(s => s with
        {
            Profiles = s.Profiles
                .Select(p => p.Id == SettingsNormalizer.DefaultProfileId && builtInNames.Contains(p.Name) ? p with { Name = wanted } : p)
                .ToList(),
        });
    }

    private void ApplyTrayTexts() =>
        _host?.Services.GetRequiredService<TrayIconService>().SetTexts(
            Loc.T("tray.show"), Loc.T("menu.settings"), Loc.T("menu.widgets"), Loc.T("menu.exit"),
            $"{ProductInfo.Name} — {Loc.T("tray.tagline")}");

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

    /// <summary>The global shortcut opens the launcher; it follows the setting live.</summary>
    private void StartLauncherShortcut(ISettingsService settings, AppShell shell)
    {
        var hotkey = _host!.Services.GetRequiredService<IGlobalHotkey>();
        hotkey.Pressed += (_, _) => shell.ToggleLauncher();
        hotkey.Register(settings.Current.LauncherHotkey);

        // Clipboard history and its shortcut follow their settings too.
        var clipboard = _host.Services.GetRequiredService<ClipboardService>();
        hotkey.ClipboardPressed += (_, _) => shell.ToggleClipboard();
        clipboard.SetEnabled(settings.Current.ClipboardHistory);
        hotkey.RegisterClipboard(settings.Current.ClipboardHotkey);
        settings.Changed += (_, args) =>
        {
            if (args.Previous.LauncherHotkey != args.Current.LauncherHotkey)
            {
                hotkey.Register(args.Current.LauncherHotkey);
            }

            if (args.Previous.ClipboardHistory != args.Current.ClipboardHistory)
            {
                clipboard.SetEnabled(args.Current.ClipboardHistory);
            }

            if (args.Previous.ClipboardHotkey != args.Current.ClipboardHotkey)
            {
                hotkey.RegisterClipboard(args.Current.ClipboardHotkey);
            }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _showSignalRegistration?.Unregister(null);
            _showSignal?.Dispose();
            _settingsSignalRegistration?.Unregister(null);
            _settingsSignal?.Dispose();
            _exitSignalRegistration?.Unregister(null);
            _exitSignal?.Dispose();

            if (_host is not null)
            {
                _host.Services.GetRequiredService<TrayIconService>().Dispose();
                _host.Services.GetRequiredService<BarManager>().Dispose(); // extra bars first
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
