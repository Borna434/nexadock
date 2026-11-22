using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NexaDock.UI.Behaviors;

/// <summary>
/// Smooth hover/press feedback for dock tiles: lifts and scales the icon, fades in the hover plate.
/// Honours <see cref="ReducedMotionProperty"/> by applying the end state instantly.
/// </summary>
public static class HoverMotion
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(HoverMotion), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.RegisterAttached(
        "ReducedMotion", typeof(bool), typeof(HoverMotion), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Element whose opacity is animated (hover plate). Optional.</summary>
    public static readonly DependencyProperty PlateProperty = DependencyProperty.RegisterAttached(
        "Plate", typeof(UIElement), typeof(HoverMotion), new PropertyMetadata(null));

    /// <summary>Element that is scaled/lifted (icon). Optional.</summary>
    public static readonly DependencyProperty ContentElementProperty = DependencyProperty.RegisterAttached(
        "ContentElement", typeof(UIElement), typeof(HoverMotion), new PropertyMetadata(null));

    public static readonly DependencyProperty HoverScaleProperty = DependencyProperty.RegisterAttached(
        "HoverScale", typeof(double), typeof(HoverMotion), new PropertyMetadata(1.12));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);
    public static bool GetReducedMotion(DependencyObject d) => (bool)d.GetValue(ReducedMotionProperty);
    public static void SetReducedMotion(DependencyObject d, bool v) => d.SetValue(ReducedMotionProperty, v);
    public static UIElement? GetPlate(DependencyObject d) => (UIElement?)d.GetValue(PlateProperty);
    public static void SetPlate(DependencyObject d, UIElement? v) => d.SetValue(PlateProperty, v);
    public static UIElement? GetContentElement(DependencyObject d) => (UIElement?)d.GetValue(ContentElementProperty);
    public static void SetContentElement(DependencyObject d, UIElement? v) => d.SetValue(ContentElementProperty, v);
    public static double GetHoverScale(DependencyObject d) => (double)d.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject d, double v) => d.SetValue(HoverScaleProperty, v);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            fe.MouseEnter += OnEnter;
            fe.MouseLeave += OnLeave;
            fe.PreviewMouseLeftButtonDown += OnDown;
            fe.PreviewMouseLeftButtonUp += OnUp;
        }
        else
        {
            fe.MouseEnter -= OnEnter;
            fe.MouseLeave -= OnLeave;
            fe.PreviewMouseLeftButtonDown -= OnDown;
            fe.PreviewMouseLeftButtonUp -= OnUp;
        }
    }

    private static void OnEnter(object sender, MouseEventArgs e) => Animate((FrameworkElement)sender, hovered: true, pressed: false);

    private static void OnLeave(object sender, MouseEventArgs e) => Animate((FrameworkElement)sender, hovered: false, pressed: false);

    private static void OnDown(object sender, MouseButtonEventArgs e) => Animate((FrameworkElement)sender, hovered: true, pressed: true);

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        Animate(fe, hovered: fe.IsMouseOver, pressed: false);
    }

    private static void Animate(FrameworkElement host, bool hovered, bool pressed)
    {
        var reduced = GetReducedMotion(host);
        var duration = reduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(pressed ? 90 : 180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if ((GetPlate(host) ?? FindPart(host, "Plate")) is { } plate)
        {
            Run(plate, UIElement.OpacityProperty, hovered ? 1.0 : 0.0, duration, ease);
        }

        if ((GetContentElement(host) ?? FindPart(host, "Content")) is FrameworkElement content)
        {
            var (scale, lift) = EnsureTransforms(content);
            var target = pressed ? 0.92 : hovered ? (reduced ? 1.0 : GetHoverScale(host)) : 1.0;
            var liftTarget = hovered && !pressed && !reduced ? -2.5 : 0.0;
            Run(scale, ScaleTransform.ScaleXProperty, target, duration, ease);
            Run(scale, ScaleTransform.ScaleYProperty, target, duration, ease);
            Run(lift, TranslateTransform.YProperty, liftTarget, duration, ease);
        }
    }

    private static UIElement? FindPart(FrameworkElement host, string name) =>
        host is System.Windows.Controls.Control { Template: { } template } control
            ? template.FindName(name, control) as UIElement
            : null;

    private static (ScaleTransform Scale, TranslateTransform Lift) EnsureTransforms(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] })
        {
            return (s, t);
        }

        var scale = new ScaleTransform(1, 1);
        var lift = new TranslateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, lift } };
        return (scale, lift);
    }

    private static void Run(IAnimatable target, DependencyProperty property, double to, TimeSpan duration, IEasingFunction ease)
    {
        if (duration == TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            return;
        }

        target.BeginAnimation(property, new DoubleAnimation(to, new Duration(duration)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }
}
