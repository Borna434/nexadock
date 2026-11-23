using System.Windows;
using System.Windows.Controls;

namespace NexaDock.UI.Controls;

/// <summary>
/// Horizontal panel for the bar: fixed tiles keep their natural width, children marked
/// <see cref="IsFlexibleProperty"/> share whatever width is left (flexible spaces).
/// </summary>
public sealed class BarPanel : Panel
{
    public const double MinFlexibleWidth = 12;

    public static readonly DependencyProperty IsFlexibleProperty = DependencyProperty.RegisterAttached(
        "IsFlexible", typeof(bool), typeof(BarPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(BarPanel),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static bool GetIsFlexible(DependencyObject d) => (bool)d.GetValue(IsFlexibleProperty);

    public static void SetIsFlexible(DependencyObject d, bool value) => d.SetValue(IsFlexibleProperty, value);

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        var visible = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            visible++;
            width += GetIsFlexible(child) ? MinFlexibleWidth : child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        width += Math.Max(0, visible - 1) * Gap;

        // Never ask for more than is available: WPF would otherwise arrange (and clip) at the
        // desired width and the overflow logic in ArrangeOverride would never see the shortage.
        // With infinite width (compact-mode measuring) the full content width is reported.
        if (!double.IsInfinity(availableSize.Width))
        {
            width = Math.Min(width, availableSize.Width);
        }

        return new Size(width, double.IsInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
    }

    /// <summary>Width kept free at the end for the overflow button when not everything fits.</summary>
    public static readonly DependencyProperty OverflowReserveProperty = DependencyProperty.Register(
        nameof(OverflowReserve), typeof(double), typeof(BarPanel),
        new FrameworkPropertyMetadata(36.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double OverflowReserve
    {
        get => (double)GetValue(OverflowReserveProperty);
        set => SetValue(OverflowReserveProperty, value);
    }

    /// <summary>Data items of the tiles hidden in the last arrange (empty when everything fits).</summary>
    public IReadOnlyList<object> HiddenItems { get; private set; } = [];

    /// <summary>Raised when the set of hidden tiles changes.</summary>
    public event EventHandler? OverflowChanged;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = ArrangeTiles(finalSize, reserve: 0, out var hidden);
        if (hidden.Count > 0)
        {
            size = ArrangeTiles(finalSize, OverflowReserve, out hidden);
        }

        if (!hidden.SequenceEqual(HiddenItems))
        {
            HiddenItems = hidden;
            Dispatcher.BeginInvoke(() => OverflowChanged?.Invoke(this, EventArgs.Empty));
        }

        return size;
    }

    private Size ArrangeTiles(Size finalSize, double reserve, out List<object> hidden)
    {
        hidden = [];
        var width = Math.Max(0, finalSize.Width - reserve);
        double fixedWidth = 0;
        var flexible = 0;
        var visible = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            visible++;
            if (GetIsFlexible(child))
            {
                flexible++;
            }
            else
            {
                fixedWidth += child.DesiredSize.Width;
            }
        }

        var gaps = Math.Max(0, visible - 1) * Gap;
        var flexWidth = flexible == 0 ? 0 : Math.Max(MinFlexibleWidth, (width - fixedWidth - gaps) / flexible);

        // Without flexible space, centre nothing: tiles start at the left like a taskbar.
        double x = 0;
        var overflowing = false;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var w = GetIsFlexible(child) ? flexWidth : child.DesiredSize.Width;
            var h = Math.Min(child.DesiredSize.Height, finalSize.Height);

            // A tile that would only partly fit is not shown at all (no tiles cut off at the edge);
            // the bar offers it through the overflow button instead.
            if (overflowing || x + w > width + 0.5)
            {
                overflowing = true; // keep order: everything after the first misfit goes too
                child.Arrange(new Rect(finalSize.Width, 0, 0, 0));
                if (child is FrameworkElement { DataContext: { } item } && !GetIsFlexible(child))
                {
                    hidden.Add(item);
                }

                continue;
            }

            child.Arrange(new Rect(x, (finalSize.Height - h) / 2, w, h));
            x += w + Gap;
        }

        return finalSize;
    }
}
