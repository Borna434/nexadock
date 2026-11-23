using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexaDock.Core;
using NexaDock.Core.Abstractions;

namespace NexaDock.Windows.Services;

/// <summary>
/// Weather from Open-Meteo (free, no API key). Only the city name and its coordinates are sent;
/// results are cached for 15 minutes.
/// </summary>
public sealed class OpenMeteoWeatherService : IWeatherService, IDisposable
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(15);
    private readonly HttpClient _http;
    private readonly ILogger<OpenMeteoWeatherService> _logger;
    private readonly ConcurrentDictionary<string, WeatherReport> _cache = new(StringComparer.OrdinalIgnoreCase);

    public OpenMeteoWeatherService(ILogger<OpenMeteoWeatherService> logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{ProductInfo.Name}/{ProductInfo.Version}");
    }

    public async Task<WeatherReport> GetAsync(string city, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            throw new WeatherException("No city set.");
        }

        if (_cache.TryGetValue(city, out var cached) && DateTimeOffset.Now - cached.FetchedAt < CacheTime)
        {
            return cached;
        }

        try
        {
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name={Uri.EscapeDataString(city.Trim())}";
            using var geo = JsonDocument.Parse(await _http.GetStringAsync(geoUrl, cancellationToken).ConfigureAwait(false));
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                throw new WeatherException($"City \"{city}\" was not found.");
            }

            var place = results[0];
            var lat = place.GetProperty("latitude").GetDouble();
            var lon = place.GetProperty("longitude").GetDouble();
            var name = place.GetProperty("name").GetString() ?? city;

            var fahrenheit = !RegionInfo.CurrentRegion.IsMetric;
            var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}")
                + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=5&timezone=auto"
                + (fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : string.Empty);

            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
            var current = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.GetProperty("daily");

            var days = new List<WeatherDay>();
            var dates = daily.GetProperty("time");
            for (var i = 0; i < dates.GetArrayLength(); i++)
            {
                days.Add(new WeatherDay(
                    DateOnly.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture),
                    daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                    daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                    daily.GetProperty("weather_code")[i].GetInt32()));
            }

            var report = new WeatherReport(
                name,
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("apparent_temperature").GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                current.GetProperty("wind_speed_10m").GetDouble(),
                current.GetProperty("relative_humidity_2m").GetInt32(),
                current.GetProperty("is_day").GetInt32() == 1,
                days,
                DateTimeOffset.Now);

            _cache[city] = report;
            return report;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Weather request failed.");
            throw new WeatherException("Weather is unavailable right now.", ex);
        }
    }

    public void Dispose() => _http.Dispose();
}
