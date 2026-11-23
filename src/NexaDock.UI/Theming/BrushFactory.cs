using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Models;
using NexaDock.Core.Theming;

namespace NexaDock.UI.Theming;

/// <summary>Builds the frozen WPF brushes that make up the layered glass surface and its tiles.</summary>
public sealed class GlassBrushes
{
    public required bool IsDark { get; init; }
    public required Brush Tint { get; init; }
    public required Brush Sheen { get; init; }
    public required Brush Rim { get; init; }
    public required Brush InnerGlow { get; init; }

    /// <summary>A bright hairline along the top edge that fades out towards the ends (specular light).</summary>
    public required Brush Specular { get; init; }

    /// <summary>Darkens the lower inner edge so the glass reads as thick, like a lens.</summary>
    public required Brush Depth { get; init; }

    /// <summary>Colour of the soft light that follows the pointer across the bar.</summary>
    public required Color LightColor { get; init; }

    /// <summary>Edge light of floating glass: bright top-left, quiet middle, a glint bottom-right.</summary>
    public required Brush EdgeLight { get; init; }
    public required Brush Foreground { get; init; }
    public required Brush SecondaryForeground { get; init; }
    public required Brush Accent { get; init; }
    public required Color AccentColor { get; init; }
    public required Brush TileFill { get; init; }
    public required Brush TileRim { get; init; }
    public required Brush TileHover { get; init; }
    public required Brush HoverFill { get; init; }
    public required Brush Separator { get; init; }
    public required Brush Track { get; init; }
    public required Color ShadowColor { get; init; }

    public static GlassBrushes Create(ThemePalette palette, double opacity, BackdropKind backdrop)
    {
        // Without blur behind it, the surface needs more body to keep text legible.
        var baseAlpha = Math.Clamp(opacity * palette.TintStrength, 0.05, 0.97);
        var tintAlpha = backdrop == BackdropKind.NativeAcrylic ? baseAlpha : 0.55 + baseAlpha * 0.4;
        var top = Parse(palette.TintTop);
        var bottom = Parse(palette.TintBottom);
        var highlight = Parse(palette.Highlight);
        var fg = Parse(palette.Foreground);
        var glow = Parse(palette.Glow);
        var accent = Parse(palette.Accent);
        var dark = palette.IsDark;
        var tile = palette.TileStrength;

        var tint = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(top, tintAlpha), 0),
                new GradientStop(WithAlpha(bottom, tintAlpha), 1),
            },
        };

        // Upper "liquid" sheen with a soft horizon, like light caught on curved glass.
        var sheenStrength = dark ? 0.16 : 0.5;
        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, sheenStrength), 0),
                new GradientStop(WithAlpha(highlight, sheenStrength * 0.3), 0.45),
                new GradientStop(WithAlpha(highlight, 0), 0.6),
                new GradientStop(WithAlpha(highlight, 0), 1),
            },
        };

        var rim = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, dark ? 0.38 : 0.95), 0),
                new GradientStop(WithAlpha(highlight, dark ? 0.08 : 0.35), 0.5),
                new GradientStop(WithAlpha(highlight, dark ? 0.18 : 0.55), 1),
            },
        };

        var innerGlow = new RadialGradientBrush
        {
            Center = new Point(0.5, 1.25),
            GradientOrigin = new Point(0.5, 1.25),
            RadiusX = 0.6,
            RadiusY = 1.2,
            GradientStops =
            {
                new GradientStop(WithAlpha(glow, dark ? 0.22 : 0.18), 0),
                new GradientStop(WithAlpha(glow, 0), 1),
            },
        };

        // Tiles: a slightly lifted plate with its own rim, the "liquid drop" inside the bar.
        var tileFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, (dark ? 0.13 : 0.62) * tile), 0),
                new GradientStop(WithAlpha(highlight, (dark ? 0.06 : 0.38) * tile), 1),
            },
        };

        var tileRim = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, dark ? 0.42 : 0.98), 0),
                new GradientStop(WithAlpha(highlight, dark ? 0.10 : 0.50), 0.35),
                new GradientStop(WithAlpha(highlight, dark ? 0.04 : 0.35), 0.7),
                new GradientStop(WithAlpha(highlight, dark ? 0.22 : 0.70), 1),
            },
        };

        var specular = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, 0), 0),
                new GradientStop(WithAlpha(highlight, dark ? 0.55 : 0.95), 0.18),
                new GradientStop(WithAlpha(highlight, dark ? 0.30 : 0.70), 0.5),
                new GradientStop(WithAlpha(highlight, dark ? 0.55 : 0.95), 0.82),
                new GradientStop(WithAlpha(highlight, 0), 1),
            },
        };

        var shade = dark ? Color.FromRgb(0, 0, 0) : Color.FromRgb(40, 60, 90);
        var depth = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(shade, 0), 0),
                new GradientStop(WithAlpha(shade, 0), 0.62),
                new GradientStop(WithAlpha(shade, dark ? 0.16 : 0.07), 1),
            },
        };

        var edgeLight = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(highlight, dark ? 0.50 : 0.98), 0),
                new GradientStop(WithAlpha(highlight, dark ? 0.12 : 0.45), 0.3),
                new GradientStop(WithAlpha(highlight, dark ? 0.05 : 0.30), 0.7),
                new GradientStop(WithAlpha(highlight, dark ? 0.30 : 0.75), 1),
            },
        };

        var hoverBase = dark ? highlight : fg;
        return new GlassBrushes
        {
            IsDark = dark,
            Tint = Freeze(tint),
            Sheen = Freeze(sheen),
            Rim = Freeze(rim),
            InnerGlow = Freeze(innerGlow),
            Specular = Freeze(specular),
            Depth = Freeze(depth),
            EdgeLight = Freeze(edgeLight),
            LightColor = highlight,
            Foreground = Freeze(new SolidColorBrush(fg)),
            SecondaryForeground = Freeze(new SolidColorBrush(Parse(palette.SecondaryForeground))),
            Accent = Freeze(new SolidColorBrush(accent)),
            AccentColor = accent,
            TileFill = Freeze(tileFill),
            TileRim = Freeze(tileRim),
            TileHover = Freeze(new SolidColorBrush(WithAlpha(hoverBase, dark ? 0.20 : 0.10))),
            HoverFill = Freeze(new LinearGradientBrush(WithAlpha(hoverBase, dark ? 0.20 : 0.10), WithAlpha(hoverBase, dark ? 0.09 : 0.05), 90)),
            Separator = Freeze(new SolidColorBrush(WithAlpha(fg, 0.16))),
            Track = Freeze(new SolidColorBrush(WithAlpha(fg, dark ? 0.16 : 0.12))),
            ShadowColor = dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 30, 45, 70),
        };
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

/// <summary>
/// Publishes brushes and metrics as dynamic resources (keys "Bar.*") so the bar, cards and
/// previews share one look and update instantly when settings change.
/// </summary>
public static class ThemeResources
{
    public static void Apply(ResourceDictionary target, GlassBrushes b, DockMetrics m, double spacing)
    {
        target["Bar.Foreground"] = b.Foreground;
        target["Bar.Secondary"] = b.SecondaryForeground;
        target["Bar.Accent"] = b.Accent;
        target["Bar.AccentColor"] = b.AccentColor;
        target["Bar.TileFill"] = b.TileFill;
        target["Bar.TileRim"] = b.TileRim;
        target["Bar.TileHover"] = b.TileHover;
        target["Bar.HoverFill"] = b.HoverFill;
        target["Bar.Separator"] = b.Separator;
        target["Bar.Track"] = b.Track;
        target["Bar.Tint"] = b.Tint;
        target["Bar.Sheen"] = b.Sheen;
        target["Bar.Rim"] = b.Rim;
        target["Bar.InnerGlow"] = b.InnerGlow;

        target["Bar.Height"] = m.Height;
        target["Bar.TileHeight"] = m.TileHeight;
        target["Bar.IconSize"] = m.IconSize;
        target["Bar.AppButton"] = m.AppButtonSize;
        target["Bar.PrimaryFont"] = m.PrimaryFontSize;
        target["Bar.SecondaryFont"] = m.SecondaryFontSize;
        target["Bar.TileRadius"] = new CornerRadius(Math.Round(m.TileHeight * 0.3));
        target["Bar.TilePadding"] = new Thickness(Math.Round(m.TileHeight * 0.26), 0, Math.Round(m.TileHeight * 0.26), 0);
        target["Bar.Spacing"] = spacing;
        target["Bar.ArtSize"] = Math.Round(m.TileHeight - 10);
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

    /// <summary>Decodes encoded image bytes (PNG/JPEG, e.g. album art).</summary>
    public static BitmapSource? DecodeImage(byte[]? bytes, int decodeWidth = 256)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = decodeWidth;
            img.StreamSource = new System.IO.MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Device pixels an icon of the given DIP size occupies on screen.</summary>
    public static int IconPixelSize(double iconDip, double scale) =>
        Math.Clamp((int)Math.Round(iconDip * Math.Max(0.5, scale)), 16, 256);
}
