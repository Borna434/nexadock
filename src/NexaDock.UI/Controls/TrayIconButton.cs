using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexaDock.UI.Services;

namespace NexaDock.UI.Controls;

/// <summary>
/// A notification-area icon. Mouse input goes straight to the owning app (left click, double
/// click, right click for its own menu, hover for its tooltip), as on the Windows taskbar.
/// In edit mode (<see cref="IsEditing"/>) a click pins or unpins the icon instead.
/// </summary>
public sealed class TrayIconButton : Button
{
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(TrayIconButton), new PropertyMetadata(false));

    public static readonly DependencyProperty HostProperty = DependencyProperty.Register(
        nameof(Host), typeof(TrayIconHost), typeof(TrayIconButton), new PropertyMetadata(null));

    /// <summary>Raised after a left click reached the app (the card may close).</summary>
    public static readonly RoutedEvent ActivatedEvent = EventManager.RegisterRoutedEvent(
        nameof(Activated), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TrayIconButton));

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public TrayIconHost? Host
    {
        get => (TrayIconHost?)GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    public event RoutedEventHandler Activated
    {
        add => AddHandler(ActivatedEvent, value);
        remove => RemoveHandler(ActivatedEvent, value);
    }

    private TrayIconItem? Item => DataContext as TrayIconItem;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (Item is not { } item || IsEditing)
        {
            return;
        }

        UpdatePlacement(item);
        item.MouseEnter();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsEditing)
        {
            Item?.MouseLeave();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsEditing)
        {
            Item?.MouseMove();
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (Item is not { } item || e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle))
        {
            return;
        }

        if (!IsEditing)
        {
            UpdatePlacement(item);
            item.MouseDown(e.ChangedButton);
        }

        // The bar must not start a tile drag or open its own menu for this press.
        e.Handled = e.ChangedButton != MouseButton.Left;
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (Item is not { } item || e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle))
        {
            return;
        }

        e.Handled = true;
        if (IsEditing)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                Host?.TogglePin(item);
            }

            return;
        }

        item.MouseUp(e.ChangedButton);
        if (e.ChangedButton == MouseButton.Left)
        {
            RaiseEvent(new RoutedEventArgs(ActivatedEvent, this));
        }
    }

    private void UpdatePlacement(TrayIconItem item)
    {
        if (PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        item.SetPlacement((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }
}
