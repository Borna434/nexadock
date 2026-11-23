using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.UI.Widgets;

namespace NexaDock.UI.Views;

public sealed record PreviewItem(IntPtr Handle, string Title, ImageSource? Icon);

/// <summary>
/// Live thumbnails of an app's windows (rendered by DWM into this window). Click a thumbnail to
/// switch to that window, or × to close it.
/// </summary>
public partial class PreviewWindow : Window
{
    private const int Gap = 8;
    private const int MaxWindows = 5;
    private readonly IBackdropService _backdrop;
    private readonly IDockWindowHost _host;
    private readonly IThumbnailService _thumbnails;
    private readonly BackdropKind _kind;
    private readonly List<IntPtr> _registered = [];
    private IntPtr _hwnd;
    private bool _dark;
    private AppsWidget? _owner;
    private double _scale = 1;

    public PreviewWindow(IBackdropService backdrop, IDockWindowHost host, IThumbnailService thumbnails, BackdropKind kind)
    {
        _backdrop = backdrop;
        _host = host;
        _thumbnails = thumbnails;
        _kind = kind;
        AllowsTransparency = kind == BackdropKind.TranslucentFallback;
        InitializeComponent();

        var radius = new CornerRadius(kind == BackdropKind.NativeAcrylic ? 8 : 16);
        Glass.CornerRadius = radius;
        Sheen.CornerRadius = radius;
        Rim.CornerRadius = radius;
    }

    public event EventHandler? PointerEntered;

    public event EventHandler? PointerLeft;

    public event EventHandler? WindowChosen;

    public void ApplyChrome(bool dark)
    {
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

    public void ShowFor(AppItemViewModel app, AppsWidget owner, PixelRect anchor, PixelRect bar, DockEdge edge, PixelRect area, double scale)
    {
        _owner = owner;
        _scale = scale;
        ClearThumbnails();
        Items.ItemsSource = app.Windows.Take(MaxWindows).Select(w => new PreviewItem(w.Handle, w.Title, app.Icon)).ToList();

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var x = Math.Clamp(anchor.X + anchor.Width / 2 - w / 2, area.X + Gap, Math.Max(area.X + Gap, area.Right - w - Gap));
        var y = edge == DockEdge.Top ? bar.Bottom + Gap : bar.Y - Gap - h;
        _host.SetBounds(_hwnd, new PixelRect(x, y, w, h));

        Dispatcher.BeginInvoke(RegisterThumbnails, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void HidePreview()
    {
        ClearThumbnails();
        Items.ItemsSource = null;
        if (IsVisible)
        {
            Hide();
        }
    }

    private void RegisterThumbnails()
    {
        ClearThumbnails();
        foreach (var item in Items.Items.OfType<PreviewItem>())
        {
            if (Items.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter container
                || FindThumb(container) is not { } thumb)
            {
                continue;
            }

            var origin = thumb.TransformToAncestor(this).Transform(new Point(0, 0));
            var slot = new PixelRect(
                (int)Math.Round(origin.X * _scale),
                (int)Math.Round(origin.Y * _scale),
                (int)Math.Round(thumb.ActualWidth * _scale),
                (int)Math.Round(thumb.ActualHeight * _scale));

            var id = _thumbnails.Register(_hwnd, item.Handle, slot);
            if (id == IntPtr.Zero)
            {
                continue;
            }

            // Fit the source inside the slot, keeping its aspect ratio.
            var (sw, sh) = _thumbnails.GetSourceSize(id);
            if (sw > 0 && sh > 0)
            {
                var fit = Math.Min((double)slot.Width / sw, (double)slot.Height / sh);
                var fw = (int)(sw * fit);
                var fh = (int)(sh * fit);
                _thumbnails.Unregister(id);
                id = _thumbnails.Register(_hwnd, item.Handle, new PixelRect(slot.X + (slot.Width - fw) / 2, slot.Y + (slot.Height - fh) / 2, fw, fh));
            }

            if (id != IntPtr.Zero)
            {
                _registered.Add(id);
            }
        }
    }

    private static FrameworkElement? FindThumb(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Tag: "Thumb" } fe)
            {
                return fe;
            }

            if (FindThumb(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void ClearThumbnails()
    {
        foreach (var id in _registered)
        {
            _thumbnails.Unregister(id);
        }

        _registered.Clear();
    }

    private void OnActivateClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PreviewItem item)
        {
            _owner?.Activate(item.Handle);
            WindowChosen?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PreviewItem item)
        {
            _owner?.CloseWindow(item.Handle);
            WindowChosen?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        PointerEntered?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        PointerLeft?.Invoke(this, EventArgs.Empty);
    }
}
