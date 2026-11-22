using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Theming;
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
/// updates the dock immediately and persists after a short debounce.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IMonitorProvider _monitors;
    private readonly IShortcutCatalog _catalog;
    private readonly IIconProvider _icons;
    private readonly IBackdropService _backdrop;
    private readonly string _dataDirectory;
    private bool _syncing;

    public SettingsViewModel(
        ISettingsService settings,
        IMonitorProvider monitors,
        IShortcutCatalog catalog,
        IIconProvider icons,
        IBackdropService backdrop,
        AppDataLocation dataLocation)
    {
        _settings = settings;
        _monitors = monitors;
        _catalog = catalog;
        _icons = icons;
        _backdrop = backdrop;
        _dataDirectory = dataLocation.Directory;

        foreach (var p in ThemePalettes.All)
        {
            var glass = GlassBrushes.Create(p, 0.9, BackdropKind.TranslucentFallback);
            Themes.Add(new ThemeOptionViewModel(p, glass.Tint, glass.Foreground));
        }

        RefreshMonitors();
        SyncFromSettings(_settings.Current);
        _settings.Changed += OnSettingsChanged;
    }

    public string Title => $"{ProductInfo.Name} Settings";
    public string VersionText => $"{ProductInfo.DisplayTitle} · {ProductInfo.Phase}";
    public string BackdropDescription => _backdrop.Capabilities.Description;
    public string DataDirectory => _dataDirectory;

    public double MinWidth => SettingsLimits.MinWidth;
    public double MaxWidth => SettingsLimits.MaxWidth;
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
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _spacing;
    [ObservableProperty] private double _opacity;
    [ObservableProperty] private double _edgeOffset;
    [ObservableProperty] private bool _reducedMotion;
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
    partial void OnWidthChanged(double value) => Apply(s => s with { Width = Math.Round(value) });
    partial void OnSpacingChanged(double value) => Apply(s => s with { Spacing = Math.Round(value) });
    partial void OnOpacityChanged(double value) => Apply(s => s with { Opacity = Math.Round(value, 2) });
    partial void OnEdgeOffsetChanged(double value) => Apply(s => s with { EdgeOffset = Math.Round(value) });
    partial void OnReducedMotionChanged(bool value) => Apply(s => s with { ReducedMotion = value });

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
            Monitors.Add(new MonitorOption(null, "Primary display (follows Windows)"));
            var preferred = _settings.Current.MonitorDeviceName;
            var list = _monitors.GetMonitors();
            foreach (var m in list)
            {
                Monitors.Add(new MonitorOption(m.DeviceName, m.DisplayLabel));
            }

            if (preferred is not null && list.All(m => !string.Equals(m.DeviceName, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                // Keep the disconnected choice visible so it is not silently lost.
                Monitors.Add(new MonitorOption(preferred, $"{preferred} (disconnected — using primary)"));
            }

            SelectedMonitor = Monitors.FirstOrDefault(m => string.Equals(m.DeviceName, preferred, StringComparison.OrdinalIgnoreCase)) ?? Monitors[0];
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    // ---- Shortcuts -------------------------------------------------------------------------------
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
            ? $"Added {added.Count} shortcut{(added.Count == 1 ? string.Empty : "s")}."
            : $"Added {added.Count}; the dock holds at most {SettingsLimits.MaxShortcuts} shortcuts.";
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
        StatusText = $"Removed {item.Name}.";
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
        StatusText = $"Restored {defaults.Count} default shortcuts.";
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
            Width = d.Width,
            Spacing = d.Spacing,
            Opacity = d.Opacity,
            EdgeOffset = d.EdgeOffset,
            Edge = d.Edge,
            MonitorDeviceName = d.MonitorDeviceName,
            ReducedMotion = d.ReducedMotion,
        });
        RefreshMonitors();
        StatusText = "Appearance and position reset to defaults. Shortcuts were kept.";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Process.Start(new ProcessStartInfo { FileName = _dataDirectory, UseShellExecute = true })?.Dispose();
    }

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
            Width = s.Width;
            Spacing = s.Spacing;
            Opacity = s.Opacity;
            EdgeOffset = s.EdgeOffset;
            ReducedMotion = s.ReducedMotion;

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

    public void Dispose() => _settings.Changed -= OnSettingsChanged;
}

/// <summary>Where local data lives; supplied by the composition root.</summary>
public sealed record AppDataLocation(string Directory);
