using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.UI.Converters;
using NexaDock.UI.Theming;
using NexaDock.UI.ViewModels;

namespace NexaDock.UI.Views;

/// <summary>
/// The launcher: a glass search box in the upper middle of the display the pointer is on.
/// Opens on the global shortcut, closes on Esc, after running something, or when it loses focus.
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _viewModel;
    private readonly DockViewModel _dock;
    private readonly IBackdropService _backdrop;
    private readonly IMonitorProvider _monitors;
    private IntPtr _hwnd;

    public LauncherWindow(LauncherViewModel viewModel, DockViewModel dock, IBackdropService backdrop, IMonitorProvider monitors)
    {
        _viewModel = viewModel;
        _dock = dock;
        _backdrop = backdrop;
        _monitors = monitors;
        AllowsTransparency = backdrop.PreferredKind == BackdropKind.TranslucentFallback;
        InitializeComponent();
        DataContext = viewModel;
        ApplyTheme();
        _dock.AppearanceChanged += (_, _) => ApplyTheme();
        viewModel.Done += (_, _) => Hide();
        Deactivated += (_, _) => Hide();
    }

    private void ApplyTheme()
    {
        if (_dock.CardBrushes is { } brushes)
        {
            ThemeResources.Apply(Resources, brushes, _dock.Metrics, _dock.Spacing);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Out of Alt+Tab and the taskbar, like the bar's own pop-ups.
        var ex = GetWindowLongPtr(_hwnd, -20).ToInt64();
        SetWindowLongPtr(_hwnd, -20, new IntPtr((ex | 0x80) & ~0x40000)); // WS_EX_TOOLWINDOW, not APPWINDOW

        if (_backdrop.PreferredKind == BackdropKind.NativeAcrylic)
        {
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            _backdrop.TryApply(_hwnd, new BackdropOptions(_dock.IsDarkTheme));
        }
    }

    /// <summary>Opens on the display under the pointer, a fifth of the way down; closes if already open.</summary>
    public void Toggle() => Toggle(clipboard: false);

    /// <summary>Opens search, or the clipboard history; the same shortcut again closes it.</summary>
    public void Toggle(bool clipboard)
    {
        if (IsVisible)
        {
            var sameMode = _viewModel.IsClipboardMode == clipboard;
            Hide();
            if (sameMode)
            {
                return;
            }
        }

        var previous = GetForegroundWindow();

        GetCursorPos(out var p);
        var monitor = Core.Layout.MonitorSelector.FromPoint(_monitors.GetMonitors(), p.X, p.Y);
        var scale = monitor?.DpiScale > 0 ? monitor.DpiScale : 1;
        _viewModel.SetDpiScale(scale);
        _viewModel.Open(clipboard, previous);
        if (monitor is not null)
        {
            var work = monitor.WorkArea;
            Left = (work.X + work.Width / 2.0) / scale - Width / 2;
            Top = (work.Y + work.Height * 0.2) / scale;
        }

        if (_hwnd != IntPtr.Zero)
        {
            _backdrop.SetDarkMode(_hwnd, _dock.IsDarkTheme);
        }

        Show();
        Activate();
        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd); // the shortcut press gave us the right to take focus
        }

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.Activate(null, openFolder: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                e.Handled = true;
                break;
            case Key.P when Keyboard.Modifiers == ModifierKeys.Control && _viewModel.IsClipboardMode:
                _viewModel.PinSelected();
                e.Handled = true;
                break;
            case Key.Delete when Keyboard.Modifiers == ModifierKeys.Shift && _viewModel.IsClipboardMode:
                _viewModel.DeleteSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherRow row })
        {
            _viewModel.Activate(row);
        }
    }

    private void OnRowHover(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherRow row })
        {
            _viewModel.Select(_viewModel.Rows.IndexOf(row));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
