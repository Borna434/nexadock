using System.Globalization;
using NexaDock.Core.Launcher;

namespace NexaDock.Tests;

public class LauncherTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2^10", 1024)]
    [InlineData("=10/4", 2.5)]
    [InlineData("2,5*2", 5)]
    [InlineData("1.234,5+0,5", 1235)]
    [InlineData("200*15%", 30)]
    [InlineData("sqrt(16)+1", 5)]
    [InlineData("-3+5", 2)]
    [InlineData("12 × 7", 84)]
    [InlineData("2*pi", 6.283185307179586)]
    public void Calculator_evaluates_expressions(string input, double expected)
    {
        Assert.True(Calculator.TryEvaluate(input, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-5")]
    [InlineData("chrome")]
    [InlineData("1/0")]
    [InlineData("2+")]
    [InlineData("(1+2")]
    [InlineData("sqrt(-1)")]
    [InlineData("foo(2)")]
    public void Calculator_ignores_non_calculations(string input)
    {
        Assert.False(Calculator.TryEvaluate(input, out _));
    }

    [Theory]
    [InlineData("10 km to mi", "6.2137119224 mi")]
    [InlineData("5 kg u lb", "11.0231131092 lb")]
    [InlineData("100 c in f", "212 °F")]
    [InlineData("2,5 GB to MB", "2,500 MB")]
    [InlineData("90 min to h", "1.5 h")]
    [InlineData("1 GiB to MB", "1,073.741824 MB")]
    [InlineData("100 km/h to mph", "62.1371192237 mph")]
    public void Units_convert(string input, string expected)
    {
        Assert.True(UnitConverter.TryConvert(input, Inv, out var result, out _));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("10 km to kg")]
    [InlineData("10 parsec to km")]
    [InlineData("km to mi")]
    public void Unit_nonsense_is_ignored(string input)
    {
        Assert.False(UnitConverter.TryConvert(input, Inv, out _, out _));
    }

    [Theory]
    [InlineData("code", "Visual Studio Code")]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("stu cod", "Visual Studio Code")]
    [InlineData("chrme", "Google Chrome")]
    [InlineData("cetvrtak", "Četvrtak")]
    [InlineData("disc", "Discord")]
    public void Fuzzy_matching_finds_what_people_type(string query, string name)
    {
        Assert.True(FuzzyMatcher.Score(query, name) > 0);
    }

    [Fact]
    public void Better_matches_rank_higher()
    {
        Assert.True(FuzzyMatcher.Score("word", "Word") > FuzzyMatcher.Score("word", "WordPad"));
        Assert.True(FuzzyMatcher.Score("word", "WordPad") > FuzzyMatcher.Score("word", "Microsoft Word"));
        Assert.True(FuzzyMatcher.Score("word", "Microsoft Word") > FuzzyMatcher.Score("word", "Password Manager"));
        Assert.Equal(0, FuzzyMatcher.Score("xyz", "Discord"));
    }

    [Fact]
    public void Engine_puts_calculations_first_then_apps_and_always_offers_the_web()
    {
        var apps = new[] { new AppEntry("Calculator", "shell:AppsFolder\\calc"), new AppEntry("Discord", "C:\\discord.exe") };

        var calc = LauncherEngine.Query("2+2", apps, new Dictionary<string, int>(), Inv);
        Assert.Equal(LauncherItemKind.Calculation, calc[0].Kind);
        Assert.Equal("4", calc[0].CopyText);
        Assert.Equal(LauncherItemKind.Web, calc[^1].Kind);

        var disc = LauncherEngine.Query("disc", apps, new Dictionary<string, int>(), Inv);
        Assert.Equal("Discord", disc[0].Title);
        Assert.Contains("google.com/search?q=disc", disc[^1].Target);
    }

    [Fact]
    public void Frequently_launched_apps_rank_higher()
    {
        var apps = new[] { new AppEntry("Paint", "paint"), new AppEntry("Paint 3D", "paint3d") };
        var usage = new Dictionary<string, int> { ["paint3d"] = 10 };

        Assert.Equal("Paint 3D", LauncherEngine.Query("paint", apps, usage, Inv)[0].Title);
    }
}
