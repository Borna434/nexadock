using System.Windows;
using System.Windows.Media;
using NexaDock.Core.Widgets;

namespace NexaDock.UI.Controls;

/// <summary>NexaDock's own vector weather icons (sun, moon, clouds, rain, snow, storm, fog).</summary>
public sealed class WeatherIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WeatherKind), typeof(WeatherIcon), new FrameworkPropertyMetadata(WeatherKind.Sun, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush SunFill = Frozen(new RadialGradientBrush(Color.FromRgb(0xFF, 0xE3, 0x7A), Color.FromRgb(0xFF, 0xA8, 0x1F)));
    private static readonly Pen RayPen = FrozenPen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x3D)), 1.6);
    private static readonly Brush MoonFill = Frozen(new LinearGradientBrush(Color.FromRgb(0xF4, 0xF1, 0xD8), Color.FromRgb(0xC9, 0xC3, 0x9A), 90));
    private static readonly Brush CloudFill = Frozen(new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC8, 0xD3, 0xE3), 90));
    private static readonly Brush DarkCloudFill = Frozen(new LinearGradientBrush(Color.FromRgb(0xC9, 0xD1, 0xDE), Color.FromRgb(0x7D, 0x8A, 0x9F), 90));
    private static readonly Pen RainPen = FrozenPen(new SolidColorBrush(Color.FromRgb(0x5A, 0xB4, 0xFF)), 1.6);
    private static readonly Brush SnowFill = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)));
    private static readonly Brush BoltFill = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x3F)));
    private static readonly Pen FogPen = FrozenPen(new SolidColorBrush(Color.FromArgb(0xE0, 0xD6, 0xDE, 0xEA)), 1.8);

    public WeatherKind Kind
    {
        get => (WeatherKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0)
        {
            return;
        }

        // Draw in a 24×24 design space.
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s / 24, s / 24));

        switch (Kind)
        {
            case WeatherKind.Sun:
                Sun(dc, 12, 12, 5.2, rays: true);
                break;
            case WeatherKind.Moon:
                Moon(dc, 12, 12, 7);
                break;
            case WeatherKind.SunCloud:
                Sun(dc, 9, 9, 4.2, rays: true);
                Cloud(dc, 4, 10, CloudFill);
                break;
            case WeatherKind.MoonCloud:
                Moon(dc, 9, 8.5, 5);
                Cloud(dc, 4, 10, CloudFill);
                break;
            case WeatherKind.Cloud:
                Cloud(dc, 2.5, 6, CloudFill);
                break;
            case WeatherKind.Fog:
                Cloud(dc, 2.5, 3, CloudFill);
                dc.DrawLine(FogPen, new Point(4, 18), new Point(20, 18));
                dc.DrawLine(FogPen, new Point(6, 21.5), new Point(18, 21.5));
                break;
            case WeatherKind.Rain:
                Cloud(dc, 2.5, 3, DarkCloudFill);
                foreach (var x in new[] { 8.0, 12.5, 17.0 })
                {
                    dc.DrawLine(RainPen, new Point(x, 17), new Point(x - 1.5, 21.5));
                }

                break;
            case WeatherKind.Snow:
                Cloud(dc, 2.5, 3, CloudFill);
                foreach (var (x, y) in new[] { (8.0, 18.5), (12.5, 20.5), (17.0, 18.5) })
                {
                    dc.DrawEllipse(SnowFill, null, new Point(x, y), 1.4, 1.4);
                }

                break;
            case WeatherKind.Storm:
                Cloud(dc, 2.5, 2.5, DarkCloudFill);
                var bolt = Geometry.Parse("M13,14 L9,19.5 L12,19.5 L10.5,23.5 L15.5,17.5 L12.5,17.5 L14.5,14 Z");
                dc.DrawGeometry(BoltFill, null, bolt);
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static void Sun(DrawingContext dc, double cx, double cy, double r, bool rays)
    {
        if (rays)
        {
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                dc.DrawLine(RayPen,
                    new Point(cx + Math.Cos(a) * (r + 1.6), cy + Math.Sin(a) * (r + 1.6)),
                    new Point(cx + Math.Cos(a) * (r + 3.4), cy + Math.Sin(a) * (r + 3.4)));
            }
        }

        dc.DrawEllipse(SunFill, null, new Point(cx, cy), r, r);
    }

    private static void Moon(DrawingContext dc, double cx, double cy, double r)
    {
        var outer = new EllipseGeometry(new Point(cx, cy), r, r);
        var bite = new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85);
        dc.DrawGeometry(MoonFill, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, bite));
    }

    /// <summary>A cloud whose bounding box starts at (x, y) and is about 19×11 units.</summary>
    private static void Cloud(DrawingContext dc, double x, double y, Brush fill)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new EllipseGeometry(new Point(x + 6, y + 7.5), 4.2, 3.6));
        g.Children.Add(new EllipseGeometry(new Point(x + 10.5, y + 5), 5, 4.8));
        g.Children.Add(new EllipseGeometry(new Point(x + 14.8, y + 7.6), 4, 3.5));
        g.Children.Add(new RectangleGeometry(new Rect(x + 6, y + 7.5, 8.8, 3.6)));
        dc.DrawGeometry(fill, null, g);
    }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Brush b, double thickness)
    {
        var p = new Pen(b, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }
}
