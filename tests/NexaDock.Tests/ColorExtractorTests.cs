using NexaDock.Core.Theming;

namespace NexaDock.Tests;

public class ColorExtractorTests
{
    private static byte[] Image(params (Rgb Color, int Count)[] parts)
    {
        var list = new List<byte>();
        foreach (var (c, n) in parts)
        {
            for (var i = 0; i < n; i++)
            {
                list.AddRange([c.B, c.G, c.R, 255]);
            }
        }

        return list.ToArray();
    }

    [Fact]
    public void The_most_present_vivid_colour_wins_over_greys()
    {
        var blue = new Rgb(30, 90, 220);
        var pixels = Image((new Rgb(128, 128, 128), 500), (new Rgb(10, 10, 10), 300), (blue, 120), (new Rgb(220, 40, 40), 40));

        var c = ColorExtractor.Dominant(pixels)!.Value;

        Assert.Equal(blue, c);
    }

    [Fact]
    public void An_all_grey_image_has_no_dominant_colour()
    {
        Assert.Null(ColorExtractor.Dominant(Image((new Rgb(120, 120, 120), 100), (new Rgb(250, 250, 250), 100))));
    }

    [Fact]
    public void Accents_keep_the_hue_but_are_vivid_and_bright()
    {
        var dull = new Rgb(60, 70, 50);
        var accent = ColorExtractor.ToAccent(dull);
        var (h1, _, _) = ColorExtractor.ToHsv(dull.R, dull.G, dull.B);
        var (h2, s2, v2) = ColorExtractor.ToHsv(accent.R, accent.G, accent.B);

        Assert.InRange(Math.Abs(h1 - h2), 0, 3);
        Assert.True(s2 >= 0.49);
        Assert.True(v2 >= 0.71);
    }

    [Fact]
    public void Hsv_round_trips()
    {
        var c = new Rgb(200, 120, 40);
        var (h, s, v) = ColorExtractor.ToHsv(c.R, c.G, c.B);
        Assert.Equal(c, ColorExtractor.FromHsv(h, s, v));
    }
}
