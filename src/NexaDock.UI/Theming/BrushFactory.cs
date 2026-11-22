using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Theming;

namespace NexaDock.UI.Theming;

/// <summary>Builds the frozen WPF brushes that make up the layered glass surface.</summary>
public sealed class GlassBrushes
{
    public required Brush Tint { get; init; }
    public required Brush Sheen { get; init; }
    public required Brush Rim { get; init; }
    public required Brush InnerGlow { get; init; }
    public required Brush Foreground { get; init; }
    public required Brush SecondaryForeground { get; init; }
    public required Brush Accent { get; init; }
    public required Brush HoverFill { get; init; }
    public required Brush HoverRim { get; init; }
    public required Brush Separator { get; init; }
    public required Color ShadowColor { get; init; }

    public static GlassBrushes Create(ThemePalette palette, double opacity, BackdropKind backdrop)
    {
        // Without blur behind it, the surface needs more body to keep text legible.
        var tintAlpha = backdrop == BackdropKind.NativeAcrylic ? opacity : 0.55 + opacity * 0.4;
        var top = Parse(palette.TintTop);
        var bottom = Parse(palette.TintBottom);
        var highlight = Parse(palette.Highlight);
        var fg = Parse(palette.Foreground);
        var glow = Parse(palette.Glow);
        var dark = palette.IsDark;

        var tint = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.35, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(top, tintAlpha), 0),
                new GradientStop(WithAlpha(bottom, tintAlpha), 1),
            },
        };

        // Upper "liquid" sheen with a soft horizon, like light caught on curved glass.
        var sheenStrength = dark ? 0.20 : 0.55;
        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, sheenStrength), 0),
                new GradientStop(WithAlpha(highlight, sheenStrength * 0.35), 0.42),
                new GradientStop(WithAlpha(highlight, 0), 0.62),
                new GradientStop(WithAlpha(highlight, 0), 1),
            },
        };

        var rim = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, dark ? 0.42 : 0.95), 0),
                new GradientStop(WithAlpha(highlight, dark ? 0.10 : 0.35), 0.5),
                new GradientStop(WithAlpha(highlight, dark ? 0.22 : 0.55), 1),
            },
        };

        var innerGlow = new RadialGradientBrush
        {
            Center = new Point(0.5, 1.15),
            GradientOrigin = new Point(0.5, 1.15),
            RadiusX = 0.55,
            RadiusY = 1.1,
            GradientStops =
            {
                new GradientStop(WithAlpha(glow, dark ? 0.30 : 0.22), 0),
                new GradientStop(WithAlpha(glow, 0), 1),
            },
        };

        var hoverBase = dark ? highlight : fg;
        var result = new GlassBrushes
        {
            Tint = Freeze(tint),
            Sheen = Freeze(sheen),
            Rim = Freeze(rim),
            InnerGlow = Freeze(innerGlow),
            Foreground = Freeze(new SolidColorBrush(fg)),
            SecondaryForeground = Freeze(new SolidColorBrush(Parse(palette.SecondaryForeground))),
            Accent = Freeze(new SolidColorBrush(Parse(palette.Accent))),
            HoverFill = Freeze(new LinearGradientBrush(WithAlpha(hoverBase, dark ? 0.20 : 0.10), WithAlpha(hoverBase, dark ? 0.08 : 0.04), 90)),
            HoverRim = Freeze(new SolidColorBrush(WithAlpha(highlight, dark ? 0.28 : 0.9))),
            Separator = Freeze(new SolidColorBrush(WithAlpha(fg, 0.16))),
            ShadowColor = dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 30, 45, 70),
        };
        return result;
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

public static class IconImageExtensions
{
    public static BitmapSource? ToBitmapSource(this IconImage? icon)
    {
        if (icon is null || icon.Width <= 0 || icon.Height <= 0)
        {
            return null;
        }

        var bmp = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Pbgra32, null, icon.Pixels, icon.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Picks a shell icon size that renders crisply for the given DIP size and scale.</summary>
    public static int IconRequestSize(double iconDip, double scale)
    {
        var px = iconDip * Math.Max(1, scale);
        foreach (var bucket in new[] { 32, 48, 64, 96, 128, 256 })
        {
            if (bucket >= px)
            {
                return bucket;
            }
        }

        return 256;
    }
}
