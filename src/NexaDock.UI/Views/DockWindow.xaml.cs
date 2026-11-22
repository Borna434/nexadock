using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Models;
using NexaDock.UI.ViewModels;

namespace NexaDock.UI.Views;

/// <summary>
/// The dock surface. View logic only: native integration, backdrop and placement are delegated to
/// injected services so they can be replaced or tested independently.
/// </summary>
public partial class DockWindow : Window
{
    private readonly DockViewModel _viewModel;
    private readonly IBackdropService _backdrop;
    private readonly IDockWindowHost _host;
    private readonly IMonitorProvider _monitors;
    private readonly IDockPositioner _positioner;
    private readonly ISettingsService _settings;
    private readonly ILogger<DockWindow> _logger;
    private IntPtr _hwnd;
    private bool _lastDark;
    private bool _repositionQueued;

    public DockWindow(
        DockViewModel viewModel,
        IBackdropService backdrop,
        IDockWindowHost host,
        IMonitorProvider monitors,
        IDockPositioner positioner,
        ISettingsService settings,
        ILogger<DockWindow> logger)
    {
        _viewModel = viewModel;
        _backdrop = backdrop;
        _host = host;
        _monitors = monitors;
        _positioner = positioner;
        _settings = settings;
        _logger = logger;

        // Must be decided before the HWND exists: layered windows cannot use the DWM system backdrop.
        var kind = backdrop.PreferredKind;
        AllowsTransparency = kind == BackdropKind.TranslucentFallback;
        _viewModel.SetBackdrop(kind);

        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.LayoutInvalidated += (_, _) => QueueReposition();
        _host.PlacementInvalidated += (_, _) => Reposition();
        DpiChanged += (_, _) => QueueReposition();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                QueueReposition();
            }
        };
    }

    public IntPtr Handle => _hwnd;

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
                // DWM refused at runtime. The window is not layered, so render the glass opaque-ish.
                _logger.LogWarning("Native backdrop unavailable at runtime; using painted surface.");
                _viewModel.SetBackdrop(BackdropKind.TranslucentFallback);
                Background = _viewModel.Brushes?.Tint ?? Brushes.Black;
            }
        }

        _host.Attach(_hwnd);
        Reposition();
    }

    /// <summary>Computes and applies the dock bounds for the configured monitor and edge.</summary>
    public void Reposition()
    {
        _repositionQueued = false;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var settings = _settings.Current;
        var monitors = _monitors.GetMonitors();
        var monitor = MonitorSelector.Select(monitors, settings.MonitorDeviceName);
        if (monitor is null)
        {
            _logger.LogWarning("No monitors reported; keeping current position.");
            return;
        }

        _viewModel.SetDpiScale(monitor.DpiScale);
        var bounds = _positioner.Calculate(monitor, settings, _viewModel.RequiredWidth, _viewModel.ShadowPadding);
        _host.SetBounds(_hwnd, bounds);

        var top = settings.Edge == DockEdge.Top;
        Resources["DockToolTipPlacement"] = top ? PlacementMode.Bottom : PlacementMode.Top;
        Resources["DockToolTipOffset"] = top ? 6.0 : -6.0;

        if (_viewModel.Backdrop == BackdropKind.NativeAcrylic && _lastDark != _viewModel.IsDarkTheme)
        {
            _lastDark = _viewModel.IsDarkTheme;
            _backdrop.SetDarkMode(_hwnd, _lastDark);
        }

        _logger.LogDebug("Dock placed on {Monitor} at {Bounds}.", monitor.DeviceName, bounds);
    }

    private void QueueReposition()
    {
        if (_repositionQueued)
        {
            return;
        }

        _repositionQueued = true;
        Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Background);
    }
}
