using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Models;
using NexaDock.UI.Localization;
using NexaDock.UI.Theming;
using NexaDock.UI.ViewModels;
using NexaDock.UI.Widgets;

namespace NexaDock.UI.Views;

/// <summary>
/// The bar. View logic only: native integration, placement and data come from injected services.
/// Handles tile clicks (cards), drag-to-reorder, drag-to-edge, context menus, window previews and
/// hiding while a full-screen app is in front.
/// </summary>
public partial class DockWindow : Window
{
    private const double DragThreshold = 6;

    private readonly DockViewModel _viewModel;
    private readonly IBackdropService _backdrop;
    private readonly IDockWindowHost _host;
    private readonly IMonitorProvider _monitors;
    private readonly IDockPositioner _positioner;
    private readonly ISettingsService _settings;
    private readonly IWindowTracker _tracker;
    private readonly ILogger<DockWindow> _logger;
    private readonly NexaDock.UI.Services.TrayIconHost _trayHost;
    private readonly BarTarget _target;
    private readonly CardWindow _card;
    private readonly PreviewWindow _preview;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _previewHideTimer;
    private readonly DispatcherTimer _contentWatch;

    private IntPtr _hwnd;
    private bool _lastDark;
    private bool? _lastFlush;
    private bool _repositionQueued;
    private bool _fullscreenHidden;
    private double _lastContentWidth;
    private MonitorInfo? _monitor;
    private PixelRect _bounds;

    // Pointer gesture state.
    private Point? _pressPoint;
    private FrameworkElement? _pressTile;
    private WidgetViewModel? _pressWidget;
    private GestureMode _gesture;
    private int _dropIndex = -1;

    // Hover preview state.
    private AppItemViewModel? _hoverItem;
    private FrameworkElement? _hoverElement;
    private AppsWidget? _hoverOwner;

    private enum GestureMode
    {
        None,
        TileDrag,
        BarDrag,
    }

    public DockWindow(
        DockViewModel viewModel,
        IBackdropService backdrop,
        IDockWindowHost host,
        IMonitorProvider monitors,
        IDockPositioner positioner,
        ISettingsService settings,
        IWindowTracker tracker,
        IThumbnailService thumbnails,
        NexaDock.UI.Services.TrayIconHost trayHost,
        BarTarget target,
        ILogger<DockWindow> logger)
    {
        _target = target;
        _trayHost = trayHost;
        _viewModel = viewModel;
        _backdrop = backdrop;
        _host = host;
        _monitors = monitors;
        _positioner = positioner;
        _settings = settings;
        _tracker = tracker;
        _logger = logger;

        // Must be decided before the HWND exists: layered windows cannot use the DWM system backdrop.
        var kind = backdrop.PreferredKind;
        AllowsTransparency = kind == BackdropKind.TranslucentFallback;
        _viewModel.SetBackdrop(kind);

        InitializeComponent();
        DataContext = _viewModel;

        _card = new CardWindow(backdrop, host, kind);
        _preview = new PreviewWindow(backdrop, host, thumbnails, kind);

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(480) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            ShowPreview();
        };
        _previewHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _previewHideTimer.Tick += (_, _) =>
        {
            _previewHideTimer.Stop();
            HidePreview();
        };
        _contentWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _contentWatch.Tick += (_, _) => WatchContentWidth();

        _preview.PointerEntered += (_, _) => _previewHideTimer.Stop();
        _preview.PointerLeft += (_, _) => SchedulePreviewHide();
        _preview.WindowChosen += (_, _) => HidePreview();

        ApplyTheme();
        _viewModel.AppearanceChanged += (_, _) => ApplyTheme();
        _viewModel.LayoutInvalidated += (_, _) => QueueReposition();
        _host.PlacementInvalidated += OnPlacementInvalidated;
        _tracker.Changed += OnWindowsChanged;
        Loaded += (_, _) => Dispatcher.BeginInvoke(UpdateFullscreenState, DispatcherPriority.Background);
        DpiChanged += (_, _) => QueueReposition();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                QueueReposition();
            }
            else
            {
                _card.CloseCard();
                HidePreview();
            }
        };
    }

    public IntPtr Handle => _hwnd;

    /// <summary>The main bar, or the extra bar of one secondary display.</summary>
    public BarTarget Target => _target;

    private void OnPlacementInvalidated(object? sender, EventArgs e) => Reposition();

    private void OnWindowsChanged(object? sender, EventArgs e) => UpdateFullscreenState();

    /// <summary>True while the user chose "Hide bar" (full-screen logic will not bring it back).</summary>
    public bool UserHidden { get; set; }

    public PixelRect Bounds => _bounds;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        if (_viewModel.Backdrop == BackdropKind.NativeAcrylic)
        {
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            _lastDark = _viewModel.IsDarkTheme;
            if (!_backdrop.TryApply(_hwnd, new BackdropOptions(_lastDark)))
            {
                _logger.LogWarning("Native backdrop unavailable at runtime; using painted surface.");
                _viewModel.SetBackdrop(BackdropKind.TranslucentFallback);
                Background = _viewModel.Brushes?.Tint ?? Brushes.Black;
            }
        }

        _host.Attach(_hwnd, isMainBar: true);
        ApplyTheme(); // corner shape needs the window handle
        _viewModel.Start();
        _contentWatch.Start();
        Reposition();
        Dispatcher.BeginInvoke(AttachOverflowWatcher, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(() =>
        {
            _logger.LogInformation(
                "Bar ready: {Widgets} widgets, {Tiles} tiles, content {Width:0} DIP, host {HW:0}x{HH:0}, window {WW:0}x{WH:0}.",
                _viewModel.Widgets.Count, AllTiles().Count, WidgetHost.DesiredSize.Width,
                WidgetHost.ActualWidth, WidgetHost.ActualHeight, ActualWidth, ActualHeight);
            foreach (var t in AllTiles().Take(3))
            {
                var p = t.TranslatePoint(new Point(0, 0), this);
                _logger.LogDebug("Tile {Type} at {X:0},{Y:0} size {W:0}x{H:0} visible={V} opacity={O}",
                    t.DataContext?.GetType().Name, p.X, p.Y, t.ActualWidth, t.ActualHeight, t.IsVisible, t.Opacity);
            }
        }, DispatcherPriority.ContextIdle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _host.PlacementInvalidated -= OnPlacementInvalidated;
        _tracker.Changed -= OnWindowsChanged;
        _contentWatch.Stop();
        if (!_target.IsMain)
        {
            _viewModel.Dispose(); // an extra bar's widgets go with it
        }

        _host.RemoveAppBar(_hwnd);
        _card.Close();
        _preview.Close();
        base.OnClosed(e);
    }

    // ---- Theme & placement ---------------------------------------------------------------------

    private void ApplyTheme()
    {
        if (_viewModel.Brushes is null || _viewModel.CardBrushes is null)
        {
            return;
        }

        ThemeResources.Apply(Resources, _viewModel.Brushes, _viewModel.Metrics, _viewModel.Spacing);
        ThemeResources.Apply(_card.Resources, _viewModel.CardBrushes, _viewModel.Metrics, _viewModel.Spacing);
        ThemeResources.Apply(_preview.Resources, _viewModel.CardBrushes, _viewModel.Metrics, _viewModel.Spacing);
        _card.ApplyChrome(_viewModel.IsDarkTheme, _viewModel.ReducedMotion);
        var light = _viewModel.Brushes.LightColor;
        PointerLightInner.Color = GlassBrushes.WithAlpha(light, _viewModel.IsDarkTheme ? 0.14 : 0.40);
        PointerLightOuter.Color = GlassBrushes.WithAlpha(light, 0);
        _preview.ApplyChrome(_viewModel.IsDarkTheme);

        if (_hwnd != IntPtr.Zero && _viewModel.Backdrop == BackdropKind.NativeAcrylic && _lastFlush != _viewModel.IsFlush)
        {
            _lastFlush = _viewModel.IsFlush;
            _backdrop.SetRoundedCorners(_hwnd, !_viewModel.IsFlush);
        }

        if (_hwnd != IntPtr.Zero && _viewModel.Backdrop == BackdropKind.NativeAcrylic && _lastDark != _viewModel.IsDarkTheme)
        {
            _lastDark = _viewModel.IsDarkTheme;
            _backdrop.SetDarkMode(_hwnd, _lastDark);
        }
    }

    /// <summary>Computes and applies the bar bounds for the configured monitor and edge.</summary>
    public void Reposition()
    {
        _repositionQueued = false;
        if (_hwnd == IntPtr.Zero || _gesture == GestureMode.BarDrag)
        {
            return;
        }

        var settings = _settings.Current;
        var monitors = _monitors.GetMonitors();
        var monitor = _target.IsMain
            ? MonitorSelector.Select(monitors, settings.MonitorDeviceName)
            : monitors.FirstOrDefault(m => string.Equals(m.DeviceName, _target.Display, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            _logger.LogWarning("No monitor for this bar; keeping current position.");
            return;
        }

        _monitor = monitor;
        _viewModel.SetDpiScale(monitor.DpiScale);
        var contentWidth = MeasureContentWidth();
        _lastContentWidth = contentWidth;

        var options = new PlacementOptions(_viewModel.ShadowPadding, UseMonitorBounds: settings.ReplaceTaskbar);
        _bounds = _positioner.Calculate(monitor, settings, contentWidth, options);
        _host.SetBounds(_hwnd, _bounds);
        if (_target.IsMain)
        {
            _trayHost.SetHostBounds(_bounds.X, _bounds.Y, _bounds.X + _bounds.Width, _bounds.Y + _bounds.Height, settings.Edge == DockEdge.Top);
        }

        if (settings.ReplaceTaskbar)
        {
            _host.SetAppBar(_hwnd, settings.Edge, DockPositionCalculator.ReservedStrip(monitor, settings));
        }
        else
        {
            _host.RemoveAppBar(_hwnd);
        }

        var top = settings.Edge == DockEdge.Top;
        Resources["DockToolTipPlacement"] = top ? PlacementMode.Bottom : PlacementMode.Top;
        Resources["DockToolTipOffset"] = top ? 8.0 : -8.0;
        UpdateFullscreenState();
    }

    private double MeasureContentWidth()
    {
        WidgetHost.Measure(new Size(double.PositiveInfinity, _viewModel.Metrics.Height));
        var pad = _viewModel.ContentPadding;
        return Math.Ceiling(WidgetHost.DesiredSize.Width + pad.Left + pad.Right + 2);
    }

    /// <summary>In compact mode the bar follows its content (apps opening, track titles changing).</summary>
    private void WatchContentWidth()
    {
        if (_settings.Current.FullWidth || _gesture != GestureMode.None || !IsVisible)
        {
            return;
        }

        if (Math.Abs(MeasureContentWidth() - _lastContentWidth) > 1)
        {
            Reposition();
        }
    }

    private void QueueReposition()
    {
        if (_repositionQueued)
        {
            return;
        }

        _repositionQueued = true;
        Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
    }

    private void UpdateFullscreenState()
    {
        // Never Hide()/Show() while the window is still being created: a Hide during Show leaves
        // Windows showing the window while WPF considers it hidden (glass without content).
        if (_monitor is null || UserHidden || !IsLoaded)
        {
            return;
        }

        var hide = _settings.Current.HideInFullscreen && _tracker.IsForegroundFullscreen(_monitor);
        if (hide && IsVisible)
        {
            _fullscreenHidden = true;
            Hide();
        }
        else if (!hide && _fullscreenHidden)
        {
            _fullscreenHidden = false;
            Show();
        }
    }

    // ---- Pointer gestures ----------------------------------------------------------------------

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        HidePreview();
        _pressPoint = e.GetPosition(this);
        _pressTile = FindTile(e.OriginalSource as DependencyObject);
        _pressWidget = _pressTile?.DataContext as WidgetViewModel;
        _gesture = GestureMode.None;
        _logger.LogDebug("Press at {X:0},{Y:0} on {Target} (source {Source}).",
            _pressPoint.Value.X, _pressPoint.Value.Y, _pressWidget?.GetType().Name ?? "background", e.OriginalSource?.GetType().Name);

        if (_card.Current is { } open && open != _pressWidget && !IsWithin(e.OriginalSource as DependencyObject, OverflowButton))
        {
            _card.CloseCard();
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        MovePointerLight(e);

        if (e.LeftButton != MouseButtonState.Pressed || _pressPoint is null)
        {
            TrackHover(e.OriginalSource as DependencyObject);
            return;
        }

        var pos = e.GetPosition(this);
        var delta = pos - _pressPoint.Value;

        if (_gesture == GestureMode.None)
        {
            var onTile = _pressWidget is not null && _pressWidget is not SpacerWidget;
            if (onTile && Math.Abs(delta.X) > DragThreshold)
            {
                _gesture = GestureMode.TileDrag;
                _card.CloseCard();
                CaptureMouse();
            }
            else if (!onTile && _target.IsMain && (Math.Abs(delta.Y) > DragThreshold * 2 || Math.Abs(delta.X) > DragThreshold * 2))
            {
                _logger.LogDebug("Bar drag started (pressed on {Target}).", _pressWidget?.GetType().Name ?? "background");
                _gesture = GestureMode.BarDrag;
                _card.CloseCard();
                CaptureMouse();
                Opacity = 0.85;
            }
        }

        switch (_gesture)
        {
            case GestureMode.TileDrag:
                DragTile(delta.X);
                break;
            case GestureMode.BarDrag:
                DragBar();
                break;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        var gesture = _gesture;
        _gesture = GestureMode.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        switch (gesture)
        {
            case GestureMode.TileDrag:
                EndTileDrag();
                e.Handled = true;
                break;

            case GestureMode.BarDrag:
                EndBarDrag();
                e.Handled = true;
                break;

            default:
                var tile = FindTile(e.OriginalSource as DependencyObject);
                if (tile is not null && tile == _pressTile && _pressWidget is not null && !IsInsideButton(e.OriginalSource as DependencyObject, tile))
                {
                    OnTileClicked(_pressWidget, tile);
                }

                break;
        }

        _pressPoint = null;
        _pressTile = null;
        _pressWidget = null;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gesture == GestureMode.TileDrag)
        {
            ResetTileTransforms();
        }
        else if (_gesture == GestureMode.BarDrag)
        {
            Opacity = 1;
            Reposition();
        }

        _gesture = GestureMode.None;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (FindTile(e.OriginalSource as DependencyObject)?.DataContext is WidgetViewModel { HandlesWheel: true } widget)
        {
            widget.OnWheel(e.Delta);
            e.Handled = true;
        }
    }

    private void OnOverflowClick(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("Overflow clicked; {Count} hidden, card open: {Open}.", _viewModel.Overflow.Hidden.Count, _card.Current?.TypeId);
        if (_card.Current == _viewModel.Overflow)
        {
            _card.CloseCard();
            return;
        }

        HidePreview();
        _card.Open(_viewModel.Overflow, ScreenRect(OverflowButton), _bounds, _settings.Current.Edge, _monitor?.Bounds ?? _bounds, _monitor?.DpiScale ?? 1);
    }

    /// <summary>Hooks the tile panel's overflow report once the item panel exists.</summary>
    private void AttachOverflowWatcher()
    {
        if (FindChild<Controls.BarPanel>(WidgetHost) is not { } panel)
        {
            Dispatcher.BeginInvoke(AttachOverflowWatcher, DispatcherPriority.Loaded);
            return;
        }

        void Sync()
        {
            _viewModel.SetOverflow(panel.HiddenItems.OfType<WidgetViewModel>());
            if (!_viewModel.HasOverflow && _card.Current == _viewModel.Overflow)
            {
                _card.CloseCard();
            }
        }

        panel.OverflowChanged += (_, _) => Sync();
        Sync(); // the first layout pass may already have hidden tiles before we subscribed
    }

    private static T? FindChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsWithin(DependencyObject? d, DependencyObject container)
    {
        while (d is not null)
        {
            if (d == container)
            {
                return true;
            }

            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return false;
    }

    private void OnTileClicked(WidgetViewModel widget, FrameworkElement tile)
    {
        _viewModel.Pulse.Acknowledge(widget.TypeId);
        if (!widget.HasCard)
        {
            widget.OnClick();
            return;
        }

        if (_card.Current == widget)
        {
            _card.CloseCard();
            return;
        }

        _card.Open(widget, ScreenRect(tile), _bounds, _settings.Current.Edge, _monitor?.Bounds ?? _bounds, _monitor?.DpiScale ?? 1);
    }

    private void DragTile(double dx)
    {
        if (_pressTile is null)
        {
            return;
        }

        _pressTile.RenderTransform = new TranslateTransform(dx, 0);
        Panel.SetZIndex(ContainerOf(_pressTile), 10);
        _pressTile.Opacity = 0.9;

        // Where would the tile land? Compare the dragged tile's centre with the others' centres.
        var tiles = AllTiles();
        var dragged = tiles.IndexOf(_pressTile);
        var centre = _pressTile.TranslatePoint(new Point(_pressTile.ActualWidth / 2, 0), WidgetHost).X;
        var target = 0;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (i == dragged)
            {
                continue;
            }

            var other = tiles[i];
            // TranslatePoint includes the preview shift applied below; remove it to get the resting centre.
            var shiftX = (other.RenderTransform as TranslateTransform)?.X ?? 0;
            var otherCentre = other.TranslatePoint(new Point(other.ActualWidth / 2, 0), WidgetHost).X - shiftX;
            if (centre > otherCentre)
            {
                target++;
            }
        }

        _dropIndex = target;

        // Slide the neighbours out of the way to preview the drop.
        var shift = _pressTile.ActualWidth + _viewModel.Spacing;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (i == dragged)
            {
                continue;
            }

            var offset = 0.0;
            if (dragged < target && i > dragged && i <= target)
            {
                offset = -shift;
            }
            else if (dragged > target && i >= target && i < dragged)
            {
                offset = shift;
            }

            tiles[i].RenderTransform = new TranslateTransform(offset, 0);
        }
    }

    private void EndTileDrag()
    {
        var widget = _pressWidget;
        var index = _dropIndex;
        ResetTileTransforms();
        if (widget is not null && index >= 0)
        {
            _viewModel.MoveWidget(widget, index);
        }
    }

    private void ResetTileTransforms()
    {
        foreach (var t in AllTiles())
        {
            t.RenderTransform = Transform.Identity;
            t.Opacity = 1;
            Panel.SetZIndex(ContainerOf(t), 0);
        }

        _dropIndex = -1;
    }

    private void DragBar()
    {
        var p = _host.GetPointer();
        var w = _bounds.Width;
        var h = _bounds.Height;
        var monitor = MonitorSelector.FromPoint(_monitors.GetMonitors(), p.X, p.Y);
        var x = monitor is null ? p.X - w / 2 : Math.Clamp(p.X - w / 2, monitor.Bounds.X, Math.Max(monitor.Bounds.X, monitor.Bounds.Right - w));
        _host.SetBounds(_hwnd, new PixelRect(x, p.Y - h / 2, w, h));
    }

    private void EndBarDrag()
    {
        Opacity = 1;
        var p = _host.GetPointer();
        var monitors = _monitors.GetMonitors();
        var monitor = MonitorSelector.FromPoint(monitors, p.X, p.Y);
        if (monitor is null)
        {
            Reposition();
            return;
        }

        var edge = p.Y < monitor.Bounds.Y + monitor.Bounds.Height / 2 ? DockEdge.Top : DockEdge.Bottom;
        string? device = monitor.IsPrimary ? null : monitor.DeviceName;
        var before = _settings.Current;
        _settings.Update(s => s with { Edge = edge, MonitorDeviceName = device });
        if (_settings.Current.Equals(before))
        {
            Reposition(); // same place: snap back
        }
    }

    // ---- Context menus -------------------------------------------------------------------------

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (FindAncestor(e.OriginalSource as DependencyObject, fe => fe is Controls.TrayIconButton) is not null)
        {
            return; // the app shows its own menu
        }

        e.Handled = true;
        HidePreview();
        _card.CloseCard();

        var source = e.OriginalSource as DependencyObject;
        var menu = new ContextMenu();

        if (FindAppItem(source) is { } appElement && appElement.DataContext is AppItemViewModel app
            && FindTile(source)?.DataContext is AppsWidget apps)
        {
            foreach (var item in apps.GetItemMenu(app))
            {
                menu.Items.Add(Item(item.Header, item.Execute, item.IsEnabled));
            }

            menu.Items.Add(new Separator());
        }
        else if (FindTile(source)?.DataContext is WidgetViewModel widget)
        {
            foreach (var item in widget.GetMenuItems())
            {
                menu.Items.Add(Item(item.Header, item.Execute, item.IsEnabled));
            }

            menu.Items.Add(Item(Loc.F("menu.remove", Loc.T($"widget.{widget.TypeId}.name")), () => _viewModel.RemoveWidget(widget)));
            menu.Items.Add(new Separator());
        }

        // Profiles: pick one manually, or let triggers switch automatically.
        var profiles = new MenuItem { Header = Loc.T("menu.profile") };
        foreach (var p in _viewModel.Profiles)
        {
            var id = p.Id;
            var item = Item(p.Name, () => _viewModel.SelectProfile(id));
            item.IsCheckable = true;
            item.IsChecked = p.Id == _viewModel.ActiveProfileId;
            profiles.Items.Add(item);
        }

        profiles.Items.Add(new Separator());
        var auto = Item(Loc.T("menu.autoSwitch"), () => _viewModel.SetAutoSwitch(!_settings.Current.AutoSwitchProfiles));
        auto.IsCheckable = true;
        auto.IsChecked = _settings.Current.AutoSwitchProfiles;
        profiles.Items.Add(auto);
        if (_target.IsMain)
        {
            menu.Items.Add(profiles);
        }

        menu.Items.Add(Item(Loc.T("menu.widgets"), () => _viewModel.OpenWidgetsCommand.Execute(null)));
        menu.Items.Add(Item(Loc.T("menu.arrange"), _viewModel.ArrangeWidgets));
        menu.Items.Add(Item(Loc.T("menu.settings"), () => _viewModel.OpenSettingsCommand.Execute(null)));
        menu.Items.Add(Item(Loc.T("menu.hide"), () => _viewModel.HideDockCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("menu.exit"), () => _viewModel.ExitCommand.Execute(null)));

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- Window previews -------------------------------------------------------------------------

    private void TrackHover(DependencyObject? source)
    {
        var element = FindAppItem(source);
        var item = element?.DataContext as AppItemViewModel;
        if (item == _hoverItem)
        {
            if (item is not null)
            {
                _previewHideTimer.Stop();
            }

            return;
        }

        _hoverItem = item;
        _hoverElement = element;
        _hoverOwner = element is null ? null : FindTile(element)?.DataContext as AppsWidget;
        _hoverTimer.Stop();

        if (item is null)
        {
            SchedulePreviewHide();
            return;
        }

        _previewHideTimer.Stop();
        if (_preview.IsVisible)
        {
            ShowPreview(); // already previewing: switch instantly
        }
        else if (item.IsRunning)
        {
            _hoverTimer.Start();
        }
    }

    // ---- Pointer light ------------------------------------------------------------------------------

    /// <summary>A soft light that follows the pointer along the glass, like a reflection.</summary>
    private void MovePointerLight(MouseEventArgs e)
    {
        var width = Root.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var x = Math.Clamp(e.GetPosition(Root).X / width, 0, 1);
        var center = new Point(x, 0.15);
        PointerLightBrush.Center = center;
        PointerLightBrush.GradientOrigin = center;
        PointerLightBrush.RadiusX = Math.Min(0.5, 150 / width);
        if (PointerLight.Opacity < 1 && !_pointerLightShown)
        {
            _pointerLightShown = true;
            FadePointerLight(1);
        }
    }

    private bool _pointerLightShown;

    private void FadePointerLight(double to)
    {
        if (_viewModel.ReducedMotion)
        {
            PointerLight.BeginAnimation(OpacityProperty, null);
            PointerLight.Opacity = to;
            return;
        }

        PointerLight.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 160 : 320))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        });
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pointerLightShown = false;
        FadePointerLight(0);
        _hoverTimer.Stop();
        _hoverItem = null;
        SchedulePreviewHide();
    }

    private void ShowPreview()
    {
        if (_hoverItem is not { IsRunning: true } item || _hoverElement is null || _hoverOwner is null || _card.Current is not null)
        {
            HidePreview();
            return;
        }

        _preview.ShowFor(item, _hoverOwner, ScreenRect(_hoverElement), _bounds, _settings.Current.Edge, _monitor?.Bounds ?? _bounds, _monitor?.DpiScale ?? 1);
    }

    private void SchedulePreviewHide()
    {
        if (_preview.IsVisible)
        {
            _previewHideTimer.Stop();
            _previewHideTimer.Start();
        }
    }

    private void HidePreview()
    {
        _hoverTimer.Stop();
        _previewHideTimer.Stop();
        _preview.HidePreview();
    }

    // ---- Visual-tree helpers ---------------------------------------------------------------------

    private PixelRect ScreenRect(FrameworkElement element)
    {
        var scale = _monitor?.DpiScale ?? 1;
        var topLeft = element.PointToScreen(new Point(0, 0));
        return new PixelRect((int)topLeft.X, (int)topLeft.Y, (int)Math.Round(element.ActualWidth * scale), (int)Math.Round(element.ActualHeight * scale));
    }

    private static FrameworkElement? FindTile(DependencyObject? d) => FindAncestor(d, fe => fe.Tag as string == "TileHost");

    private static FrameworkElement? FindAppItem(DependencyObject? d) => FindAncestor(d, fe => fe.Tag as string == "AppItem");

    private static FrameworkElement? FindAncestor(DependencyObject? d, Func<FrameworkElement, bool> match)
    {
        while (d is not null)
        {
            if (d is FrameworkElement fe && match(fe))
            {
                return fe;
            }

            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return null;
    }

    private static bool IsInsideButton(DependencyObject? d, FrameworkElement stopAt)
    {
        while (d is not null && d != stopAt)
        {
            if (d is ButtonBase)
            {
                return true;
            }

            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return false;
    }

    private List<FrameworkElement> AllTiles()
    {
        var list = new List<FrameworkElement>();
        for (var i = 0; i < WidgetHost.Items.Count; i++)
        {
            if (WidgetHost.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter cp
                && VisualTreeHelper.GetChildrenCount(cp) > 0
                && VisualTreeHelper.GetChild(cp, 0) is FrameworkElement tile)
            {
                list.Add(tile);
            }
        }

        return list;
    }

    private static UIElement ContainerOf(FrameworkElement tile) =>
        VisualTreeHelper.GetParent(tile) as UIElement ?? tile;
}
