using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Models;
using NexaDock.Core.Theming;
using NexaDock.UI.Theming;

namespace NexaDock.UI.ViewModels;

/// <summary>State for the dock surface: shortcuts, clock, metrics and glass brushes.</summary>
public sealed partial class DockViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IAppLauncher _launcher;
    private readonly IIconProvider _icons;
    private readonly IAppShell _shell;
    private readonly ILogger<DockViewModel> _logger;
    private readonly DispatcherTimer _clock;
    private double _scale = 1.0;
    private int _iconRequestSize;

    public DockViewModel(ISettingsService settings, IAppLauncher launcher, IIconProvider icons, IAppShell shell, ILogger<DockViewModel> logger)
    {
        _settings = settings;
        _launcher = launcher;
        _icons = icons;
        _shell = shell;
        _logger = logger;

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clock.Start();

        _settings.Changed += OnSettingsChanged;
        ApplySettings(_settings.Current, rebuildShortcuts: true);
    }

    public ObservableCollection<ShortcutItemViewModel> Shortcuts { get; } = [];

    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string _dateText = string.Empty;
    [ObservableProperty] private string _dateTimeToolTip = string.Empty;

    [ObservableProperty] private DockMetrics _metrics = DockMetrics.For(DockSizePreset.Medium);
    [ObservableProperty] private Thickness _itemMargin;
    [ObservableProperty] private Thickness _contentPadding;
    [ObservableProperty] private Thickness _windowPadding;
    [ObservableProperty] private CornerRadius _cornerRadius = new(8);
    [ObservableProperty] private CornerRadius _tileCornerRadius = new(12);
    [ObservableProperty] private GlassBrushes? _brushes;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _isFallbackBackdrop;
    [ObservableProperty] private bool _hasShortcuts;

    public BackdropKind Backdrop { get; private set; } = BackdropKind.NativeAcrylic;

    /// <summary>Transparent margin (DIPs) around the glass for the fallback drop shadow.</summary>
    public double ShadowPadding => Backdrop == BackdropKind.TranslucentFallback ? 16 : 0;

    /// <summary>Minimum glass width needed for all content.</summary>
    public double RequiredWidth => Metrics.RequiredWidth(Shortcuts.Count, _settings.Current.Spacing);

    public DockThemeId Theme => _settings.Current.Theme;

    public bool IsDarkTheme => ThemePalettes.Get(_settings.Current.Theme).IsDark;

    /// <summary>Raised when something that affects window size or placement changed.</summary>
    public event EventHandler? LayoutInvalidated;

    public void SetBackdrop(BackdropKind kind)
    {
        Backdrop = kind;
        IsFallbackBackdrop = kind == BackdropKind.TranslucentFallback;
        ApplySettings(_settings.Current, rebuildShortcuts: false);
    }

    /// <summary>Called by the view when the dock moves to a monitor with a different DPI.</summary>
    public void SetDpiScale(double scale)
    {
        _scale = scale;
        var request = IconImageExtensions.IconRequestSize(Metrics.IconSize, _scale);
        if (request != _iconRequestSize)
        {
            _iconRequestSize = request;
            RefreshIcons();
        }
    }

    [RelayCommand]
    private void OpenSettings() => _shell.ShowSettings();

    [RelayCommand]
    private void Exit() => _shell.Exit();

    [RelayCommand]
    private void HideDock() => _shell.HideDock();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var shortcutsChanged = !e.Previous.Shortcuts.SequenceEqual(e.Current.Shortcuts);
        ApplySettings(e.Current, shortcutsChanged);
    }

    private void ApplySettings(DockSettings s, bool rebuildShortcuts)
    {
        var metrics = DockMetrics.For(s.Size);
        var sizeChanged = metrics != Metrics;
        Metrics = metrics;

        ItemMargin = new Thickness(s.Spacing / 2, 0, s.Spacing / 2, 0);
        ContentPadding = new Thickness(metrics.Padding + 8, metrics.Padding, metrics.Padding + 6, metrics.Padding);
        WindowPadding = new Thickness(ShadowPadding);
        CornerRadius = Backdrop == BackdropKind.NativeAcrylic
            ? new CornerRadius(8) // matches the DWM rounded-corner clip
            : new CornerRadius(metrics.Height * 0.34);
        TileCornerRadius = new CornerRadius(metrics.TileSize * 0.28);
        ReducedMotion = s.ReducedMotion;
        Brushes = GlassBrushes.Create(ThemePalettes.Get(s.Theme), s.Opacity, Backdrop);
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(IsDarkTheme));

        var request = IconImageExtensions.IconRequestSize(metrics.IconSize, _scale);
        if (rebuildShortcuts)
        {
            _iconRequestSize = request;
            RebuildShortcuts(s.Shortcuts);
        }
        else if (sizeChanged && request != _iconRequestSize)
        {
            _iconRequestSize = request;
            RefreshIcons();
        }

        OnPropertyChanged(nameof(RequiredWidth));
        LayoutInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildShortcuts(IReadOnlyList<DockShortcut> shortcuts)
    {
        Shortcuts.Clear();
        foreach (var s in shortcuts)
        {
            Shortcuts.Add(new ShortcutItemViewModel(s, LoadIcon(s.Target), Launch));
        }

        HasShortcuts = Shortcuts.Count > 0;
    }

    private void RefreshIcons()
    {
        foreach (var item in Shortcuts)
        {
            item.Icon = LoadIcon(item.Target);
        }
    }

    private ImageSource? LoadIcon(string target) =>
        _icons.GetIcon(target, _iconRequestSize == 0 ? 64 : _iconRequestSize).ToBitmapSource();

    private void Launch(DockShortcut shortcut)
    {
        var result = _launcher.Launch(shortcut);
        if (!result.Success)
        {
            _logger.LogWarning("Launch failed: {Error}", result.Error);
            _shell.NotifyError($"Could not open {shortcut.Name}", result.Error ?? "The target could not be started.");
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var time = now.ToString(culture.DateTimeFormat.ShortTimePattern.Replace(":ss", string.Empty), culture);
        if (time != TimeText)
        {
            TimeText = time;
            DateText = now.ToString("ddd d MMM", culture);
            DateTimeToolTip = now.ToString("D", culture);
        }
    }

    public void Dispose()
    {
        _clock.Stop();
        _settings.Changed -= OnSettingsChanged;
    }
}
