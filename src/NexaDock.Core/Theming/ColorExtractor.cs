namespace NexaDock.Core.Theming;

/// <summary>An RGB colour (0–255 per channel).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Picks the colour that characterises an image (album art, wallpaper): the most present
/// noticeable hue, ignoring greys, near-black and near-white, and makes a legible accent of it.
/// </summary>
public static class ColorExtractor
{
    private const int HueBins = 24;

    /// <summary>Dominant colour of 32-bit BGRA pixels, or null when the image is all grey.</summary>
    public static Rgb? Dominant(ReadOnlySpan<byte> bgra)
    {
        var weight = new double[HueBins];
        var sumR = new double[HueBins];
        var sumG = new double[HueBins];
        var sumB = new double[HueBins];

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128)
            {
                continue; // transparent
            }

            var (h, s, v) = ToHsv(bgra[i + 2], bgra[i + 1], bgra[i]);
            if (s < 0.18 || v < 0.15 || (v > 0.96 && s < 0.25))
            {
                continue;
            }

            var bin = (int)(h / 360 * HueBins) % HueBins;
            var w = s * (0.35 + v); // vivid and bright pixels count more
            weight[bin] += w;
            sumR[bin] += bgra[i + 2] * w;
            sumG[bin] += bgra[i + 1] * w;
            sumB[bin] += bgra[i] * w;
        }

        // Smooth over neighbouring bins so a gradient is not split in two.
        var best = -1;
        var bestScore = 0.0;
        for (var b = 0; b < HueBins; b++)
        {
            var score = weight[b] + 0.5 * (weight[(b + HueBins - 1) % HueBins] + weight[(b + 1) % HueBins]);
            if (score > bestScore)
            {
                bestScore = score;
                best = b;
            }
        }

        if (best < 0 || weight[best] <= 0)
        {
            return null;
        }

        return new Rgb(
            (byte)Math.Round(sumR[best] / weight[best]),
            (byte)Math.Round(sumG[best] / weight[best]),
            (byte)Math.Round(sumB[best] / weight[best]));
    }

    /// <summary>The same hue, made vivid and bright enough for an accent on dark or light glass.</summary>
    public static Rgb ToAccent(Rgb c)
    {
        var (h, s, v) = ToHsv(c.R, c.G, c.B);
        return FromHsv(h, Math.Clamp(Math.Max(s, 0.5), 0, 0.85), Math.Clamp(Math.Max(v, 0.72), 0, 0.95));
    }

    public static (double H, double S, double V) ToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == rf)
            {
                h = 60 * (((gf - bf) / d) % 6);
            }
            else if (max == gf)
            {
                h = 60 * ((bf - rf) / d + 2);
            }
            else
            {
                h = 60 * ((rf - gf) / d + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Rgb FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
