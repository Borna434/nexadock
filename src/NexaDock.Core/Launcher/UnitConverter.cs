using System.Globalization;
using System.Text.RegularExpressions;

namespace NexaDock.Core.Launcher;

/// <summary>
/// "10 km to mi", "5 kg u lb", "100 c in f", "2,5 GB to MB", "90 min to h": length, mass,
/// temperature, data, time, speed and volume. English and Croatian connectors (to, in, u, =, →).
/// </summary>
public static partial class UnitConverter
{
    private enum Dimension
    {
        Length,
        Mass,
        Temperature,
        Data,
        Time,
        Speed,
        Volume,
    }

    private sealed record Unit(Dimension Dimension, double Factor, string Symbol);

    /// <summary>Factor converts to the base unit (m, kg, B, s, m/s, l); temperature is special.</summary>
    private static readonly Dictionary<string, Unit> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = new(Dimension.Length, 0.001, "mm"),
        ["cm"] = new(Dimension.Length, 0.01, "cm"),
        ["m"] = new(Dimension.Length, 1, "m"),
        ["km"] = new(Dimension.Length, 1000, "km"),
        ["in"] = new(Dimension.Length, 0.0254, "in"),
        ["inch"] = new(Dimension.Length, 0.0254, "in"),
        ["ft"] = new(Dimension.Length, 0.3048, "ft"),
        ["yd"] = new(Dimension.Length, 0.9144, "yd"),
        ["mi"] = new(Dimension.Length, 1609.344, "mi"),
        ["mile"] = new(Dimension.Length, 1609.344, "mi"),
        ["miles"] = new(Dimension.Length, 1609.344, "mi"),

        ["mg"] = new(Dimension.Mass, 0.000001, "mg"),
        ["g"] = new(Dimension.Mass, 0.001, "g"),
        ["dag"] = new(Dimension.Mass, 0.01, "dag"),
        ["kg"] = new(Dimension.Mass, 1, "kg"),
        ["t"] = new(Dimension.Mass, 1000, "t"),
        ["lb"] = new(Dimension.Mass, 0.45359237, "lb"),
        ["lbs"] = new(Dimension.Mass, 0.45359237, "lb"),
        ["oz"] = new(Dimension.Mass, 0.028349523125, "oz"),

        ["c"] = new(Dimension.Temperature, 0, "°C"),
        ["°c"] = new(Dimension.Temperature, 0, "°C"),
        ["f"] = new(Dimension.Temperature, 0, "°F"),
        ["°f"] = new(Dimension.Temperature, 0, "°F"),
        ["k"] = new(Dimension.Temperature, 0, "K"),

        ["b"] = new(Dimension.Data, 1, "B"),
        ["kb"] = new(Dimension.Data, 1e3, "kB"),
        ["mb"] = new(Dimension.Data, 1e6, "MB"),
        ["gb"] = new(Dimension.Data, 1e9, "GB"),
        ["tb"] = new(Dimension.Data, 1e12, "TB"),
        ["kib"] = new(Dimension.Data, 1024, "KiB"),
        ["mib"] = new(Dimension.Data, 1024d * 1024, "MiB"),
        ["gib"] = new(Dimension.Data, 1024d * 1024 * 1024, "GiB"),
        ["tib"] = new(Dimension.Data, 1024d * 1024 * 1024 * 1024, "TiB"),

        ["ms"] = new(Dimension.Time, 0.001, "ms"),
        ["s"] = new(Dimension.Time, 1, "s"),
        ["sec"] = new(Dimension.Time, 1, "s"),
        ["min"] = new(Dimension.Time, 60, "min"),
        ["h"] = new(Dimension.Time, 3600, "h"),
        ["d"] = new(Dimension.Time, 86400, "d"),
        ["day"] = new(Dimension.Time, 86400, "d"),
        ["days"] = new(Dimension.Time, 86400, "d"),
        ["dana"] = new(Dimension.Time, 86400, "d"),
        ["week"] = new(Dimension.Time, 604800, "week"),
        ["tj"] = new(Dimension.Time, 604800, "tj"),

        ["kmh"] = new(Dimension.Speed, 1 / 3.6, "km/h"),
        ["km/h"] = new(Dimension.Speed, 1 / 3.6, "km/h"),
        ["mph"] = new(Dimension.Speed, 0.44704, "mph"),
        ["m/s"] = new(Dimension.Speed, 1, "m/s"),
        ["kn"] = new(Dimension.Speed, 0.514444, "kn"),

        ["ml"] = new(Dimension.Volume, 0.001, "ml"),
        ["dl"] = new(Dimension.Volume, 0.1, "dl"),
        ["l"] = new(Dimension.Volume, 1, "l"),
        ["gal"] = new(Dimension.Volume, 3.785411784, "gal"),
        ["cup"] = new(Dimension.Volume, 0.2365882365, "cup"),
    };

    [GeneratedRegex(@"^\s*(-?[\d.,]+)\s*([a-z°/]+)\s*(?:to|in|u|=|->|→)\s*([a-z°/]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryConvert(string input, CultureInfo culture, out string result, out string explained)
    {
        result = explained = string.Empty;
        var m = Pattern().Match(input);
        if (!m.Success
            || !Units.TryGetValue(m.Groups[2].Value, out var from)
            || !Units.TryGetValue(m.Groups[3].Value, out var to)
            || from.Dimension != to.Dimension
            || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        var value = from.Dimension == Dimension.Temperature
            ? FromKelvin(ToKelvin(amount, from.Symbol), to.Symbol)
            : amount * from.Factor / to.Factor;

        result = Calculator.Format(value, culture) + " " + to.Symbol;
        explained = Calculator.Format(amount, culture) + " " + from.Symbol + " = " + result;
        return true;
    }

    private static double ToKelvin(double v, string unit) => unit switch
    {
        "°C" => v + 273.15,
        "°F" => (v - 32) * 5 / 9 + 273.15,
        _ => v,
    };

    private static double FromKelvin(double k, string unit) => unit switch
    {
        "°C" => k - 273.15,
        "°F" => (k - 273.15) * 9 / 5 + 32,
        _ => k,
    };
}
