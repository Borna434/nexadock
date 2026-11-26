using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexaDock.Core.Usage;

namespace NexaDock.Infrastructure.Usage;

/// <summary>
/// Asks Claude for the plan limits, the same numbers Claude Code's /usage and the Claude app show.
/// Uses the access token Claude Code stores in <c>.credentials.json</c>; the file is only read,
/// never changed, and the token is sent only to api.anthropic.com. An expired token is not
/// refreshed here: Claude Code refreshes it the next time it runs.
/// </summary>
public sealed class ClaudeLimitsClient : IClaudeLimitsSource
{
    private const string Endpoint = "https://api.anthropic.com/api/oauth/usage";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClaudePlanLimits? _last;

    public ClaudeLimitsClient(ILogger<ClaudeLimitsClient>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<ClaudePlanLimits?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_last is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < CacheFor)
            {
                return cached;
            }

            var token = ReadToken();
            if (token is null)
            {
                return _last = null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("NexaDock");
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Claude plan limits: HTTP {Status}", (int)response.StatusCode);
                return _last = null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return _last = Parse(json, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogInformation(ex, "Claude plan limits unavailable");
            return _last = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Parses the limits response: <c>{"five_hour":{"utilization":6,"resets_at":"…"},"seven_day":{…}}</c>.</summary>
    public static ClaudePlanLimits? Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var five = Limit(root, "five_hour");
        var week = Limit(root, "seven_day");
        return five is null && week is null ? null : new ClaudePlanLimits(five, week, now);
    }

    private static PlanLimit? Limit(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!e.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        DateTimeOffset? resets = null;
        if (e.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            resets = at;
        }

        return new PlanLimit(Math.Clamp(u.GetDouble(), 0, 100), resets);
    }

    /// <summary>The access token from Claude Code's credentials file, or null when missing or expired.</summary>
    private static string? ReadToken()
    {
        foreach (var dir in ConfigDirs())
        {
            var path = Path.Combine(dir, ".credentials.json");
            if (!File.Exists(path))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number
                && DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()) <= DateTimeOffset.UtcNow)
            {
                continue;
            }

            if (oauth.TryGetProperty("accessToken", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
            {
                return t.GetString();
            }
        }

        return null;
    }

    private static IEnumerable<string> ConfigDirs() =>
        ClaudeLogReader.DefaultRoots().Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.OrdinalIgnoreCase);
}
