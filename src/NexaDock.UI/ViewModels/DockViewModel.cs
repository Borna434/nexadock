using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Models;
using NexaDock.Core.Theming;
using NexaDock.Core.Widgets;
using NexaDock.UI.Services;
using NexaDock.UI.Theming;
using NexaDock.UI.Widgets;

namespace NexaDock.UI.ViewModels;

/// <summary>State for the bar: the widget instances, theme brushes and metrics.</summary>
public sealed partial class DockViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly WidgetFactory _factory;
    private readonly IAppShell _shell;
    private readonly ILogger<DockViewModel> _logger;
    private double _scale = 1.0;
    private bool _started;

    private readonly PulseCenter _pulse;

    private readonly WallpaperAccent _wallpaper;

    public DockViewModel(ISettingsService settings, WidgetFactory factory, IAppShell shell, PulseCenter pulse, BarTarget target, WallpaperAccent wallpaper, ILogger<DockViewModel> logger)
    {
        _wallpaper = wallpaper;
        _wallpaper.Changed += OnWallpaperChanged;
        _settings = settings;
        _factory = factory;
        _shell = shell;
        _pulse = pulse;
        Target = target;
        _logger = logger;
        _settings.Changed += OnSettingsChanged;
        _pulse.Changed += OnPulseChanged;
        Localization.Loc.LanguageChanged += OnLanguageChanged;
        ApplyAppearance(_settings.Current);
    }

    /// <summary>The main bar, or the extra bar of one secondary display.</summary>
    public BarTarget Target { get; }

    public bool IsMainBar => Target.IsMain;

    private IReadOnlyList<WidgetPlacement> Layout(DockSettings s) => BarLayouts.Get(s, Target.Display);

    private void OnPulseChanged(object? sender, EventArgs e) => UpdatePulseGlow();

    private void OnWallpaperChanged(object? sender, EventArgs e) => ApplyAppearance(_settings.Current);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var w in Widgets)
        {
            w.OnLanguageChanged();
            w.ApplySettings(_settings.Current);
        }
    }

    public PulseCenter Pulse => _pulse;

    /// <summary>Card content for the "»" button: tiles that did not fit on the bar.</summary>
    public OverflowWidget Overflow { get; } = new();

    [ObservableProperty] private bool _hasOverflow;

    public void SetOverflow(IEnumerable<WidgetViewModel> hidden)
    {
        Overflow.SetHidden(hidden);
        HasOverflow = Overflow.Hidden.Count > 0;
    }

    /// <summary>Profiles for the bar's quick menu.</summary>
    public IReadOnlyList<DockProfile> Profiles => _settings.Current.Profiles;

    public string? ActiveProfileId => _settings.Current.ActiveProfileId;

    /// <summary>Manual choice: becomes the default profile and is applied right away.</summary>
    public void SelectProfile(string id) =>
        _settings.Update(s => Core.Profiles.ProfileEngine.Activate(s, id) with { DefaultProfileId = id });

    public void SetAutoSwitch(bool enabled) => _settings.Update(s => s with { AutoSwitchProfiles = enabled });

    private void UpdatePulseGlow()
    {
        foreach (var w in Widgets)
        {
            w.IsPulsing = w.TypeId != Core.Widgets.WidgetTypes.Pulse && _pulse.IsActiveFor(w.TypeId);
        }
    }

    public ObservableCollection<WidgetViewModel> Widgets { get; } = [];

    [ObservableProperty] private DockMetrics _metrics = DockMetrics.For(DockSizePreset.Medium);
    [ObservableProperty] private Thickness _contentPadding;
    [ObservableProperty] private Thickness _windowPadding;
    [ObservableProperty] private CornerRadius _cornerRadius = new(8);
    [ObservableProperty] private GlassBrushes? _brushes;
    [ObservableProperty] private GlassBrushes? _cardBrushes;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _isFallbackBackdrop;
    [ObservableProperty] private double _spacing = 6;

    /// <summary>Full width with no gap: the bar sits on the screen edge like the Windows taskbar.</summary>
    [ObservableProperty] private bool _isFlush;

    /// <summary>The rim: all round for a floating bar, one line on the inner side when flush.</summary>
    [ObservableProperty] private Thickness _rimThickness = new(1);

    /// <summary>Where the specular hairline sits: just inside the top edge, inset from the ends.</summary>
    [ObservableProperty] private Thickness _specularMargin = new(14, 1, 14, 0);

    public BackdropKind Backdrop { get; private set; } = BackdropKind.NativeAcrylic;

    /// <summary>Transparent margin (DIPs) around the glass for the fallback drop shadow.</summary>
    public double ShadowPadding => Backdrop == BackdropKind.TranslucentFallback ? 14 : 0;

    public bool IsDarkTheme => ThemePalettes.Get(_settings.Current.Theme).IsDark;

    public DockSettings Settings => _settings.Current;

    /// <summary>Raised when something that affects window size or placement changed.</summary>
    public event EventHandler? LayoutInvalidated;

    /// <summary>Raised when brushes or metrics changed (views refresh their dynamic resources).</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>Creates and starts the widgets. Call once the bar window exists.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SyncWidgets(_settings.Current);
    }

    public void SetBackdrop(BackdropKind kind)
    {
        Backdrop = kind;
        IsFallbackBackdrop = kind == BackdropKind.TranslucentFallback;
        ApplyAppearance(_settings.Current);
    }

    public void SetDpiScale(double scale)
    {
        if (Math.Abs(scale - _scale) < 0.001)
        {
            return;
        }

        _scale = scale;
        foreach (var w in Widgets)
        {
            w.SetDpiScale(scale);
        }
    }

    public void RemoveWidget(WidgetViewModel widget) =>
        _settings.Update(s => BarLayouts.With(s, Target.Display, Layout(s).Where(w => w.Id != widget.InstanceId).ToList()));

    /// <summary>Puts the widgets in their logical places around the centred apps.</summary>
    public void ArrangeWidgets() =>
        _settings.Update(s => BarLayouts.With(s, Target.Display, WidgetArranger.Arrange(Layout(s))));

    public void MoveWidget(WidgetViewModel widget, int newIndex)
    {
        _settings.Update(s =>
        {
            var list = Layout(s).ToList();
            var from = list.FindIndex(w => w.Id == widget.InstanceId);
            if (from < 0)
            {
                return s;
            }

            var item = list[from];
            list.RemoveAt(from);
            list.Insert(Math.Clamp(newIndex, 0, list.Count), item);
            return BarLayouts.With(s, Target.Display, list);
        });
    }

    [RelayCommand]
    private void OpenSettings() => _shell.ShowSettings();

    [RelayCommand]
    private void OpenLauncher() => _shell.ToggleLauncher();

    [RelayCommand]
    private void OpenWidgets() => _shell.ShowWidgetPicker(Target.Display);

    [RelayCommand]
    private void HideDock() => _shell.HideDock();

    [RelayCommand]
    private void Exit() => _shell.Exit();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        ApplyAppearance(e.Current);
        if (_started)
        {
            SyncWidgets(e.Current);
        }

        LayoutInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyAppearance(DockSettings s)
    {
        var metrics = DockMetrics.For(s.Size);
        Metrics = metrics;
        Spacing = s.Spacing;
        var vertical = (metrics.Height - metrics.TileHeight) / 2;
        ContentPadding = new Thickness(vertical + 1, vertical, vertical + 1, vertical);
        WindowPadding = new Thickness(ShadowPadding);
        IsFlush = s.FullWidth && s.EdgeOffset <= 0;
        RimThickness = !IsFlush ? new Thickness(1) : s.Edge == DockEdge.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
        SpecularMargin = IsFlush ? new Thickness(0, 1, 0, 0) : new Thickness(14, 1, 14, 0);
        CornerRadius = IsFlush
            ? new CornerRadius(0)
            : Backdrop == BackdropKind.NativeAcrylic
                ? new CornerRadius(8) // matches the DWM rounded-corner clip
                : new CornerRadius(Math.Round(metrics.Height * 0.36));
        ReducedMotion = s.ReducedMotion;

        var palette = ThemePalettes.Get(s.Theme);

        // The wallpaper's colour as accent and glow, so the glass matches what is behind it.
        if (s.AccentFromWallpaper && _wallpaper.Accent is { } accent)
        {
            var hex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
            palette = palette with { Accent = hex, Glow = hex };
        }
        Brushes = GlassBrushes.Create(palette, s.Opacity, Backdrop);
        CardBrushes = GlassBrushes.Create(palette, Math.Max(s.Opacity + 0.2, 0.7), Backdrop);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(Settings));
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes <see cref="Widgets"/> match the configured layout, reusing live instances.</summary>
    private void SyncWidgets(DockSettings s)
    {
        var wanted = Layout(s);
        foreach (var stale in Widgets.Where(w => wanted.All(p => p.Id != w.InstanceId)).ToList())
        {
            Widgets.Remove(stale);
            stale.Dispose();
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var placement = wanted[i];
            var existing = Widgets.FirstOrDefault(w => w.InstanceId == placement.Id);
            if (existing is null)
            {
                WidgetViewModel? created;
                try
                {
                    created = _factory.Create(placement);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not create widget {Type}.", placement.TypeId);
                    continue;
                }

                if (created is null)
                {
                    continue;
                }

                created.SetDisplay(Target.Display);
                created.SetDpiScale(_scale);
                created.ApplySettings(s);
                Widgets.Insert(Math.Min(i, Widgets.Count), created);
                created.Start();
            }
            else
            {
                var at = Widgets.IndexOf(existing);
                var target = Math.Min(i, Widgets.Count - 1);
                if (at != target)
                {
                    Widgets.Move(at, target);
                }

                existing.ApplySettings(s);
            }
        }

        UpdatePulseGlow();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _pulse.Changed -= OnPulseChanged;
        _wallpaper.Changed -= OnWallpaperChanged;
        Localization.Loc.LanguageChanged -= OnLanguageChanged;
        foreach (var w in Widgets)
        {
            w.Dispose();
        }

        Widgets.Clear();
    }
}
