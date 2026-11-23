using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Theming;
using NexaDock.Core.Profiles;
using NexaDock.UI.Localization;
using NexaDock.UI.Theming;

namespace NexaDock.UI.ViewModels;

public sealed partial class ThemeOptionViewModel(ThemePalette palette, Brush preview, Brush foreground) : ObservableObject
{
    public ThemePalette Palette { get; } = palette;
    public DockThemeId Id => Palette.Id;
    public string Name => Palette.DisplayName;
    public string Description => Palette.Description;
    public Brush Preview { get; } = preview;
    public Brush PreviewForeground { get; } = foreground;

    [ObservableProperty] private bool _isSelected;
}

public sealed record MonitorOption(string? DeviceName, string Label);

public sealed partial class ShortcutEditorItem(DockShortcut shortcut, ImageSource? icon) : ObservableObject
{
    public DockShortcut Shortcut { get; } = shortcut;
    public ImageSource? Icon { get; } = icon;
    public string Name => Shortcut.Name;
    public string Target => Shortcut.Target;
}

/// <summary>
/// Settings are applied live: every change goes straight to <see cref="ISettingsService"/>, which
/// updates the bar immediately and persists after a short debounce.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IMonitorProvider _monitors;
    private readonly IShortcutCatalog _catalog;
    private readonly IIconProvider _icons;
    private readonly IBackdropService _backdrop;
    private readonly IStartupRegistration _startup;
    private readonly IAppShell _shell;
    private readonly string _dataDirectory;
    private bool _syncing;

    public SettingsViewModel(
        ISettingsService settings,
        IMonitorProvider monitors,
        IShortcutCatalog catalog,
        IIconProvider icons,
        IBackdropService backdrop,
        IStartupRegistration startup,
        IAppShell shell,
        AppDataLocation dataLocation,
        NexaDock.UI.Services.CalendarFeed calendar)
    {
        Calendar = calendar;
        _settings = settings;
        _monitors = monitors;
        _catalog = catalog;
        _icons = icons;
        _backdrop = backdrop;
        _startup = startup;
        _shell = shell;
        _dataDirectory = dataLocation.Directory;

        foreach (var p in ThemePalettes.All)
        {
            var glass = GlassBrushes.Create(p, 0.75, BackdropKind.TranslucentFallback);
            Themes.Add(new ThemeOptionViewModel(p, glass.Tint, glass.Foreground));
        }

        RefreshMonitors();
        SyncFromSettings(_settings.Current);
        _settings.Changed += OnSettingsChanged;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public string Title => Loc.F("settings.title", ProductInfo.Name);
    public string VersionText => $"{ProductInfo.DisplayTitle} · {ProductInfo.Phase}";

    public IReadOnlyList<LanguageOption> Languages =>
    [
        new(AppLanguage.System, "language.system"),
        new(AppLanguage.English, "language.english"),
        new(AppLanguage.Croatian, "language.croatian"),
    ];

    public IReadOnlyList<TemplateOption> Templates =>
    [
        new(ProfileTemplate.Work, "profiles.template.work"),
        new(ProfileTemplate.Gaming, "profiles.template.gaming"),
        new(ProfileTemplate.Study, "profiles.template.study"),
        new(ProfileTemplate.Blank, "profiles.template.blank"),
    ];

    public IReadOnlyList<string> TimeOptions => ProfileItemViewModel.TimeOptions;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private bool _pulseEnabled;
    [ObservableProperty] private bool _autoSwitchProfiles;
    [ObservableProperty] private TemplateOption? _selectedTemplate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveProfileCommand), nameof(ActivateProfileCommand))]
    private ProfileItemViewModel? _selectedProfile;

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is not null)
        {
            Apply(s => s with { Language = value.Value });
        }
    }

    partial void OnPulseEnabledChanged(bool value) => Apply(s => s with { PulseEnabled = value });

    partial void OnAutoSwitchProfilesChanged(bool value) => Apply(s => s with { AutoSwitchProfiles = value });

    [RelayCommand]
    private void AddProfile()
    {
        var template = SelectedTemplate ?? Templates[0];
        if (_settings.Current.Profiles.Count >= SettingsLimits.MaxProfiles)
        {
            StatusText = Loc.F("profiles.limit", SettingsLimits.MaxProfiles);
            return;
        }

        var profile = ProfileTemplates.Create(template.Template, Loc.T(template.LabelKey));
        Apply(s => s with { Profiles = s.Profiles.Append(profile).ToList() });
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        StatusText = Loc.F("profiles.added", profile.Name);
    }

    private bool CanEditProfile => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanEditProfile))]
    private void RemoveProfile()
    {
        if (SelectedProfile is not { } item || _settings.Current.Profiles.Count <= 1)
        {
            StatusText = Loc.T("profiles.keepOne");
            return;
        }

        var id = item.Id;
        Apply(s =>
        {
            var remaining = s.Profiles.Where(p => p.Id != id).ToList();
            var next = s.ActiveProfileId == id ? ProfileEngine.Activate(s with { Profiles = s.Profiles }, remaining[0].Id) : s;
            return next with { Profiles = next.Profiles.Where(p => p.Id != id).ToList() };
        });
        StatusText = Loc.F("profiles.removed", item.Name);
    }

    [RelayCommand(CanExecute = nameof(CanEditProfile))]
    private void ActivateProfile()
    {
        if (SelectedProfile is { } item)
        {
            var id = item.Id;
            Apply(s => ProfileEngine.Activate(s, id) with { DefaultProfileId = id });
        }
    }

    private void UpdateProfile(DockProfile profile) =>
        Apply(s => s with { Profiles = s.Profiles.Select(p => p.Id == profile.Id ? profile with { Widgets = p.Widgets } : p).ToList() });

    private void SyncProfiles(DockSettings s)
    {
        var selected = SelectedProfile?.Id;
        if (Profiles.Count == s.Profiles.Count && Profiles.Select(p => p.Id).SequenceEqual(s.Profiles.Select(p => p.Id)))
        {
            for (var i = 0; i < s.Profiles.Count; i++)
            {
                if (!Profiles[i].Profile.Equals(s.Profiles[i]) || Profiles[i].IsActive != (s.Profiles[i].Id == s.ActiveProfileId))
                {
                    Profiles[i].Load(s.Profiles[i], s.Profiles[i].Id == s.ActiveProfileId);
                }
            }

            return;
        }

        Profiles.Clear();
        foreach (var p in s.Profiles)
        {
            Profiles.Add(new ProfileItemViewModel(p, p.Id == s.ActiveProfileId, UpdateProfile));
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selected) ?? Profiles.FirstOrDefault(p => p.IsActive);
    }
    public string BackdropDescription => _backdrop.Capabilities.Description;
    public string DataDirectory => _dataDirectory;

    public double MinSpacing => SettingsLimits.MinSpacing;
    public double MaxSpacing => SettingsLimits.MaxSpacing;
    public double MinOpacity => SettingsLimits.MinOpacity;
    public double MaxOpacity => SettingsLimits.MaxOpacity;
    public double MinEdgeOffset => SettingsLimits.MinEdgeOffset;
    public double MaxEdgeOffset => SettingsLimits.MaxEdgeOffset;

    public ObservableCollection<ThemeOptionViewModel> Themes { get; } = [];
    public ObservableCollection<MonitorOption> Monitors { get; } = [];
    public ObservableCollection<ShortcutEditorItem> Shortcuts { get; } = [];

    [ObservableProperty] private bool _isSmall;
    [ObservableProperty] private bool _isMedium;
    [ObservableProperty] private bool _isLarge;
    [ObservableProperty] private bool _isBottom;
    [ObservableProperty] private bool _isTop;
    [ObservableProperty] private bool _fullWidth;
    [ObservableProperty] private double _spacing;
    [ObservableProperty] private double _opacity;
    [ObservableProperty] private double _edgeOffset;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _use24HourClock;
    [ObservableProperty] private bool _replaceTaskbar;
    /// <summary>Google account and linked calendars (the Calendars tab).</summary>
    public NexaDock.UI.Services.CalendarFeed Calendar { get; }

    [ObservableProperty] private bool _hideInFullscreen;
    [ObservableProperty] private bool _showOnAllDisplays;
    [ObservableProperty] private bool _launchAtStartup;
    [ObservableProperty] private string _weatherCity = string.Empty;
    [ObservableProperty] private MonitorOption? _selectedMonitor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveShortcutCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private ShortcutEditorItem? _selectedShortcut;

    [ObservableProperty] private string _statusText = string.Empty;

    // ---- Appearance ------------------------------------------------------------------------------
    [RelayCommand]
    private void SelectTheme(ThemeOptionViewModel? option)
    {
        if (option is not null)
        {
            Apply(s => s with { Theme = option.Id });
        }
    }

    partial void OnIsSmallChanged(bool value) { if (value) Apply(s => s with { Size = DockSizePreset.Small }); }
    partial void OnIsMediumChanged(bool value) { if (value) Apply(s => s with { Size = DockSizePreset.Medium }); }
    partial void OnIsLargeChanged(bool value) { if (value) Apply(s => s with { Size = DockSizePreset.Large }); }
    partial void OnIsBottomChanged(bool value) { if (value) Apply(s => s with { Edge = DockEdge.Bottom }); }
    partial void OnIsTopChanged(bool value) { if (value) Apply(s => s with { Edge = DockEdge.Top }); }
    partial void OnFullWidthChanged(bool value) => Apply(s => s with { FullWidth = value });
    partial void OnSpacingChanged(double value) => Apply(s => s with { Spacing = Math.Round(value) });
    partial void OnOpacityChanged(double value) => Apply(s => s with { Opacity = Math.Round(value, 2) });
    partial void OnEdgeOffsetChanged(double value) => Apply(s => s with { EdgeOffset = Math.Round(value) });
    partial void OnReducedMotionChanged(bool value) => Apply(s => s with { ReducedMotion = value });
    partial void OnUse24HourClockChanged(bool value) => Apply(s => s with { Use24HourClock = value });
    partial void OnReplaceTaskbarChanged(bool value) => Apply(s => s with { ReplaceTaskbar = value });
    partial void OnHideInFullscreenChanged(bool value) => Apply(s => s with { HideInFullscreen = value });

    partial void OnShowOnAllDisplaysChanged(bool value) => Apply(s => s with { ShowOnAllDisplays = value });
    partial void OnWeatherCityChanged(string value) => Apply(s => s with { WeatherCity = value ?? string.Empty });

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        try
        {
            _startup.SetEnabled(value);
            Apply(s => s with { LaunchAtStartup = value });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            StatusText = Loc.F("settings.startupFailed", ex.Message);
        }
    }

    partial void OnSelectedMonitorChanged(MonitorOption? value)
    {
        if (value is not null)
        {
            Apply(s => s with { MonitorDeviceName = value.DeviceName });
        }
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            Monitors.Clear();
            Monitors.Add(new MonitorOption(null, Loc.T("position.primary")));
            var preferred = _settings.Current.MonitorDeviceName;
            var list = _monitors.GetMonitors();
            foreach (var m in list)
            {
                Monitors.Add(new MonitorOption(m.DeviceName, m.DisplayLabel));
            }

            if (preferred is not null && list.All(m => !string.Equals(m.DeviceName, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                // Keep the disconnected choice visible so it is not silently lost.
                Monitors.Add(new MonitorOption(preferred, Loc.F("position.disconnected", preferred)));
            }

            SelectedMonitor = Monitors.FirstOrDefault(m => string.Equals(m.DeviceName, preferred, StringComparison.OrdinalIgnoreCase)) ?? Monitors[0];
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    [RelayCommand]
    private void OpenWidgetPicker() => _shell.ShowWidgetPicker();

    // ---- Pinned apps -----------------------------------------------------------------------------
    /// <summary>Set by the view: shows a file picker and returns the chosen paths.</summary>
    public Func<IReadOnlyList<string>>? PickFiles { get; set; }

    [RelayCommand]
    private void AddShortcut()
    {
        var files = PickFiles?.Invoke() ?? [];
        if (files.Count == 0)
        {
            return;
        }

        var current = _settings.Current.Shortcuts.ToList();
        var room = SettingsLimits.MaxShortcuts - current.Count;
        var added = files.Take(Math.Max(0, room)).Select(_catalog.CreateFromFile).ToList();
        current.AddRange(added);
        Apply(s => s with { Shortcuts = current, ShortcutsInitialized = true });
        StatusText = added.Count == files.Count
            ? Loc.F("pinned.added", added.Count)
            : Loc.F("pinned.addedLimit", added.Count, SettingsLimits.MaxShortcuts);
        SelectedShortcut = Shortcuts.LastOrDefault();
    }

    private bool HasSelection => SelectedShortcut is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveShortcut()
    {
        if (SelectedShortcut is not { } item)
        {
            return;
        }

        var index = Shortcuts.IndexOf(item);
        var list = _settings.Current.Shortcuts.Where(s => s.Id != item.Shortcut.Id).ToList();
        Apply(s => s with { Shortcuts = list, ShortcutsInitialized = true });
        StatusText = Loc.F("pinned.removed", item.Name);
        SelectedShortcut = Shortcuts.Count == 0 ? null : Shortcuts[Math.Clamp(index, 0, Shortcuts.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedShortcut is not { } item)
        {
            return;
        }

        var list = _settings.Current.Shortcuts.ToList();
        var index = list.FindIndex(s => s.Id == item.Shortcut.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        (list[index], list[target]) = (list[target], list[index]);
        var id = item.Shortcut.Id;
        Apply(s => s with { Shortcuts = list });
        SelectedShortcut = Shortcuts.FirstOrDefault(s => s.Shortcut.Id == id);
    }

    [RelayCommand]
    private void RestoreDefaultShortcuts()
    {
        var defaults = _catalog.GetDefaultShortcuts();
        Apply(s => s with { Shortcuts = defaults, ShortcutsInitialized = true });
        StatusText = Loc.F("pinned.restored", defaults.Count);
    }

    // ---- General ---------------------------------------------------------------------------------
    [RelayCommand]
    private void ResetAppearance()
    {
        var d = DockSettings.Default;
        Apply(s => s with
        {
            Theme = d.Theme,
            Size = d.Size,
            FullWidth = d.FullWidth,
            Spacing = d.Spacing,
            Opacity = d.Opacity,
            EdgeOffset = d.EdgeOffset,
            Edge = d.Edge,
            MonitorDeviceName = d.MonitorDeviceName,
            ReducedMotion = d.ReducedMotion,
        });
        RefreshMonitors();
        StatusText = Loc.T("settings.resetDone");
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo { FileName = _dataDirectory, UseShellExecute = true })?.Dispose();

    // ---- Sync ------------------------------------------------------------------------------------
    private void Apply(Func<DockSettings, DockSettings> mutate)
    {
        if (!_syncing)
        {
            _settings.Update(mutate);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => SyncFromSettings(e.Current);

    private void SyncFromSettings(DockSettings s)
    {
        _syncing = true;
        try
        {
            foreach (var t in Themes)
            {
                t.IsSelected = t.Id == s.Theme;
            }

            IsSmall = s.Size == DockSizePreset.Small;
            IsMedium = s.Size == DockSizePreset.Medium;
            IsLarge = s.Size == DockSizePreset.Large;
            IsBottom = s.Edge == DockEdge.Bottom;
            IsTop = s.Edge == DockEdge.Top;
            FullWidth = s.FullWidth;
            Spacing = s.Spacing;
            Opacity = s.Opacity;
            EdgeOffset = s.EdgeOffset;
            ReducedMotion = s.ReducedMotion;
            Use24HourClock = s.Use24HourClock;
            ReplaceTaskbar = s.ReplaceTaskbar;
            HideInFullscreen = s.HideInFullscreen;
            ShowOnAllDisplays = s.ShowOnAllDisplays;
            LaunchAtStartup = s.LaunchAtStartup;
            PulseEnabled = s.PulseEnabled;
            AutoSwitchProfiles = s.AutoSwitchProfiles;
            SelectedLanguage = Languages.FirstOrDefault(l => l.Value == s.Language) ?? Languages[0];
            SyncProfiles(s);
            if (!string.Equals(WeatherCity.Trim(), s.WeatherCity, StringComparison.Ordinal))
            {
                WeatherCity = s.WeatherCity;
            }

            var selectedId = SelectedShortcut?.Shortcut.Id;
            if (!Shortcuts.Select(x => x.Shortcut).SequenceEqual(s.Shortcuts))
            {
                Shortcuts.Clear();
                foreach (var shortcut in s.Shortcuts)
                {
                    Shortcuts.Add(new ShortcutEditorItem(shortcut, _icons.GetIcon(shortcut.Target, 32).ToBitmapSource()));
                }

                SelectedShortcut = Shortcuts.FirstOrDefault(x => x.Shortcut.Id == selectedId);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(Templates));
        _syncing = true;
        try
        {
            SelectedLanguage = Languages.FirstOrDefault(l => l.Value == _settings.Current.Language);
            SelectedTemplate = null;
        }
        finally
        {
            _syncing = false;
        }

        RefreshMonitors();
        foreach (var p in Profiles)
        {
            p.Load(p.Profile, p.IsActive);
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Where local data lives; supplied by the composition root.</summary>
public sealed record AppDataLocation(string Directory);
