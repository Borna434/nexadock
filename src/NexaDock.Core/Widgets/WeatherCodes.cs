namespace NexaDock.Core.Widgets;

/// <summary>Maps WMO weather codes (as used by Open-Meteo) to words and Segoe Fluent glyphs.</summary>
public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 or 81 => "Showers",
        82 => "Violent showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown",
    };

    public static WeatherKind Kind(int code, bool isDay = true) => code switch
    {
        0 or 1 => isDay ? WeatherKind.Sun : WeatherKind.Moon,
        2 => isDay ? WeatherKind.SunCloud : WeatherKind.MoonCloud,
        3 => WeatherKind.Cloud,
        45 or 48 => WeatherKind.Fog,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherKind.Snow,
        95 or 96 or 99 => WeatherKind.Storm,
        _ => WeatherKind.Cloud,
    };
}

/// <summary>Icon families drawn by the weather widget.</summary>
public enum WeatherKind
{
    Sun,
    Moon,
    SunCloud,
    MoonCloud,
    Cloud,
    Fog,
    Rain,
    Snow,
    Storm,
}
