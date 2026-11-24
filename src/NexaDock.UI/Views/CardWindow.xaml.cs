using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.UI.Widgets;

namespace NexaDock.UI.Views;

/// <summary>
/// Glass pop-up that shows a widget's card next to its tile. Non-activating like the bar, so it
/// closes on a click anywhere outside it (polled, because inactive windows get no deactivate).
/// </summary>
public partial class CardWindow : Window
{
    private const int Gap = 8;
    private readonly IBackdropService _backdrop;
    private readonly IDockWindowHost _host;
    private readonly BackdropKind _kind;
    private IDisposable? _outsideClicks;
    private IntPtr _hwnd;
    private bool _reducedMotion;
    private bool _dark;
    private PixelRect _barBounds;
    private PixelRect _cardBounds;

    public CardWindow(IBackdropService backdrop, IDockWindowHost host, BackdropKind kind)
    {
        _backdrop = backdrop;
        _host = host;
        _kind = kind;
        AllowsTransparency = kind == BackdropKind.TranslucentFallback;
        InitializeComponent();
        AddHandler(Controls.TrayIconButton.ActivatedEvent, new RoutedEventHandler((_, _) => CloseCard()));

        var radius = new CornerRadius(kind == BackdropKind.NativeAcrylic ? 8 : 18);
        foreach (var b in new[] { Shadow, Glass, Glow, Sheen, Rim })
        {
            b.CornerRadius = radius;
        }

        if (kind == BackdropKind.TranslucentFallback)
        {
            Shadow.Visibility = Visibility.Visible;
            Root.Margin = new Thickness(16);
        }

    }

    public WidgetViewModel? Current { get; private set; }

    public void ApplyChrome(bool dark, bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        _dark = dark;
        if (_hwnd != IntPtr.Zero && _kind == BackdropKind.NativeAcrylic)
        {
            _backdrop.SetDarkMode(_hwnd, dark);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_kind == BackdropKind.NativeAcrylic)
        {
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            _backdrop.TryApply(_hwnd, new BackdropOptions(_dark));
        }

        _host.Attach(_hwnd, isMainBar: false);
    }

    /// <param name="anchor">Tile bounds in physical pixels.</param>
    public void Open(WidgetViewModel widget, PixelRect anchor, PixelRect bar, DockEdge edge, PixelRect area, double scale)
    {
        if (Current is not null && Current != widget)
        {
            Current.IsCardOpen = false;
            Current.OnCardClosed();
            Current.CloseCardRequested -= OnCloseRequested;
        }

        if (Current != widget)
        {
            widget.CloseCardRequested += OnCloseRequested;
        }

        Current = widget;
        _barBounds = bar;
        Body.Content = widget;
        widget.IsCardOpen = true;
        widget.OnCardOpened();

        Opacity = 0;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var x = Math.Clamp(anchor.X + anchor.Width / 2 - w / 2, area.X + Gap, Math.Max(area.X + Gap, area.Right - w - Gap));
        var y = edge == DockEdge.Top ? bar.Bottom + Gap : bar.Y - Gap - h;
        _cardBounds = new PixelRect(x, y, w, h);
        _host.SetBounds(_hwnd, _cardBounds);

        if (_reducedMotion)
        {
            Opacity = 1;
        }
        else
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase() });
        }

        _outsideClicks ??= _host.ObserveMouseDown(CheckDismiss);
    }

    private bool _keyboard;

    protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        // Typing needs a focusable window: switch on keyboard input only when a text field is clicked.
        if (!_keyboard && Current?.WantsKeyboard == true && FindTextBox(e.OriginalSource as DependencyObject) is { } box)
        {
            _keyboard = true;
            _host.SetKeyboardInput(_hwnd, true);
            Activate();
            box.Focus();
            System.Windows.Input.Keyboard.Focus(box);
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            CloseCard();
            e.Handled = true;
        }
    }

    private static System.Windows.Controls.Control? FindTextBox(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox)
            {
                return (System.Windows.Controls.Control)d;
            }

            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return null;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => CloseCard();

    public void CloseCard()
    {
        if (_keyboard)
        {
            _keyboard = false;
            _host.SetKeyboardInput(_hwnd, false);
        }

        _outsideClicks?.Dispose();
        _outsideClicks = null;
        if (Current is { } widget)
        {
            widget.CloseCardRequested -= OnCloseRequested;
            widget.IsCardOpen = false;
            widget.OnCardClosed();
        }

        Current = null;
        Body.Content = null;
        if (IsVisible)
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
        }
    }

    private void CheckDismiss(PointerState p)
    {
        if (Current is null)
        {
            return;
        }

        // Re-measure: cards grow when data arrives (e.g. process lists).
        var bounds = _host.GetBounds(_hwnd);
        if (bounds.Width > 0)
        {
            _cardBounds = bounds;
        }

        // Clicks on the bar are handled by the bar (toggle / switch card).
        if (!Contains(_cardBounds, p) && !Contains(_barBounds, p))
        {
            CloseCard();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        // Keep the card anchored to the bar edge when its content changes height.
        if (Current is null || _hwnd == IntPtr.Zero || !sizeInfo.HeightChanged)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var belowBar = _cardBounds.Y >= _barBounds.Bottom;
        var y = belowBar ? _cardBounds.Y : _barBounds.Y - Gap - h;
        _cardBounds = new PixelRect(_cardBounds.X, y, w, h);
        _host.SetBounds(_hwnd, _cardBounds);
    }

    private static bool Contains(PixelRect r, PointerState p) =>
        p.X >= r.X && p.X < r.Right && p.Y >= r.Y && p.Y < r.Bottom;
}
