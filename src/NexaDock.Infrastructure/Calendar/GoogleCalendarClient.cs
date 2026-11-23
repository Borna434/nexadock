using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexaDock.Core;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Calendar;

namespace NexaDock.Infrastructure.Calendar;

/// <summary>
/// Google Calendar through the user's Google account, read-only.
/// <list type="bullet">
/// <item>Sign-in: OAuth 2.0 for installed apps with PKCE and a loopback redirect
/// (http://127.0.0.1:port/), opened in the user's own browser; NexaDock never sees the password.</item>
/// <item>Only the refresh token and the client secret are stored, encrypted for this Windows user
/// (DPAPI), in the local database.</item>
/// <item>Scope: calendar.readonly.</item>
/// </list>
/// </summary>
public sealed class GoogleCalendarClient : IGoogleCalendarService
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string Api = "https://www.googleapis.com/calendar/v3";

    private const string ClientIdKey = "google.clientId";
    private const string ClientSecretKey = "google.clientSecret";
    private const string RefreshTokenKey = "google.refreshToken";
    private const string EmailKey = "google.email";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private readonly IKeyValueStore _store;
    private readonly ILogger<GoogleCalendarClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpires;

    public GoogleCalendarClient(IKeyValueStore store, ILogger<GoogleCalendarClient> logger)
    {
        _store = store;
        _logger = logger;
    }

    public bool HasClient => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

    public string? ClientId => _store.Get(ClientIdKey);

    private string? ClientSecret => Unprotect(_store.Get(ClientSecretKey));

    private string? RefreshToken => Unprotect(_store.Get(RefreshTokenKey));

    public bool IsSignedIn => !string.IsNullOrEmpty(RefreshToken);

    public string? AccountEmail => _store.Get(EmailKey);

    public void SetClient(string clientId, string clientSecret)
    {
        _store.Set(ClientIdKey, clientId.Trim());
        _store.Set(ClientSecretKey, Protect(clientSecret.Trim()));
    }

    public void ClearClient()
    {
        _store.Set(ClientIdKey, null);
        _store.Set(ClientSecretKey, null);
    }

    // ---- Sign-in ------------------------------------------------------------------------------

    public async Task SignInAsync(Action<string> openBrowser, CancellationToken cancellationToken)
    {
        var clientId = ClientId ?? throw new InvalidOperationException("No OAuth client.");
        var clientSecret = ClientSecret ?? throw new InvalidOperationException("No OAuth client.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";

        var url = AuthEndpoint
            + "?client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirect)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString(Scope)
            + "&code_challenge=" + challenge
            + "&code_challenge_method=S256"
            + "&access_type=offline"
            + "&prompt=consent"
            + "&state=" + state;
        openBrowser(url);

        var query = await ReceiveRedirectAsync(listener, cancellationToken).ConfigureAwait(false);
        if (query.TryGetValue("error", out var error))
        {
            throw new InvalidOperationException(error == "access_denied" ? "denied" : error);
        }

        if (!query.TryGetValue("state", out var returnedState) || returnedState != state || !query.TryGetValue("code", out var code))
        {
            throw new InvalidOperationException("invalid response");
        }

        using var response = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
        }), cancellationToken).ConfigureAwait(false);

        var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || !json.TryGetProperty("refresh_token", out var refresh))
        {
            throw new InvalidOperationException(ErrorOf(json) ?? $"HTTP {(int)response.StatusCode}");
        }

        _store.Set(RefreshTokenKey, Protect(refresh.GetString()!));
        StoreAccessToken(json);
        _logger.LogInformation("Signed in to Google Calendar.");

        // The primary calendar's ID is the account's address; show it in Settings.
        try
        {
            var calendars = await GetCalendarsAsync(cancellationToken).ConfigureAwait(false);
            _store.Set(EmailKey, calendars.FirstOrDefault(c => c.IsPrimary)?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogWarning("Could not read the calendar list after sign-in: {Error}", ex.Message);
        }
    }

    /// <summary>Waits for Google to redirect the browser back to us, then shows a short page.</summary>
    private static async Task<Dictionary<string, string>> ReceiveRedirectAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

            // "GET /?code=...&state=... HTTP/1.1"; browsers may also ask for /favicon.ico.
            var parts = requestLine.Split(' ');
            var target = parts.Length > 1 ? parts[1] : string.Empty;
            var queryIndex = target.IndexOf('?');
            var isCallback = target.StartsWith("/?", StringComparison.Ordinal) || (queryIndex >= 0 && target[..queryIndex] == "/");

            var ok = isCallback && target.Contains("code=", StringComparison.Ordinal);
            var body = isCallback
                ? Page(ok ? "NexaDock is connected to Google Calendar" : "Sign-in was not completed",
                       ok ? "You can close this tab and go back to NexaDock." : "You can close this tab and try again in NexaDock.")
                : string.Empty;
            var status = isCallback ? "200 OK" : "404 Not Found";
            var bytes = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

            if (!isCallback)
            {
                continue;
            }

            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in target[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                query[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;
            }

            return query;
        }
    }

    private static string Page(string title, string text) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>" + ProductInfo.Name + "</title>"
        + "<style>body{font-family:Segoe UI,sans-serif;background:#10131a;color:#eef1f7;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}"
        + "div{text-align:center}h1{font-weight:600;font-size:22px}p{opacity:.7}</style></head><body><div><h1>"
        + WebUtility.HtmlEncode(title) + "</h1><p>" + WebUtility.HtmlEncode(text) + "</p></div></body></html>";

    public async Task SignOutAsync()
    {
        var token = RefreshToken;
        _store.Set(RefreshTokenKey, null);
        _store.Set(EmailKey, null);
        _accessToken = null;

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                // Also withdraw NexaDock's access on Google's side.
                using var _ = await Http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token })).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Could not revoke the Google token: {Error}", ex.Message);
            }
        }

        _logger.LogInformation("Signed out of Google Calendar.");
    }

    // ---- Calendar API --------------------------------------------------------------------------

    public async Task<IReadOnlyList<GoogleCalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken)
    {
        var result = new List<GoogleCalendarInfo>();
        string? page = null;
        do
        {
            var url = Api + "/users/me/calendarList?maxResults=250" + (page is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(page));
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var item in Items(json))
            {
                var id = Str(item, "id");
                if (id is null || Bool(item, "deleted"))
                {
                    continue;
                }

                result.Add(new GoogleCalendarInfo(
                    id,
                    Str(item, "summaryOverride") ?? Str(item, "summary") ?? id,
                    Str(item, "backgroundColor") ?? "#4285F4",
                    Bool(item, "selected") && !Bool(item, "hidden"),
                    Bool(item, "primary")));
            }

            page = Str(json, "nextPageToken");
        }
        while (page is not null);

        // Primary first, then by name, as in Google Calendar's own list.
        return result.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(GoogleCalendarInfo calendar, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = new List<CalendarEvent>();
        string? page = null;
        var pages = 0;
        do
        {
            var url = Api + "/calendars/" + Uri.EscapeDataString(calendar.Id) + "/events"
                + "?singleEvents=true&orderBy=startTime&maxResults=250"
                + "&timeMin=" + Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                + "&timeMax=" + Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                + (page is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(page));
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var item in Items(json))
            {
                if (ToEvent(item, calendar) is { } e)
                {
                    result.Add(e);
                }
            }

            page = Str(json, "nextPageToken");
        }
        while (page is not null && ++pages < 8);

        return result;
    }

    /// <summary>Maps one Calendar API event; cancelled events and malformed ones are skipped.</summary>
    public static CalendarEvent? ToEvent(JsonElement item, GoogleCalendarInfo calendar)
    {
        if (Str(item, "status") == "cancelled"
            || !item.TryGetProperty("start", out var start)
            || !item.TryGetProperty("end", out var end))
        {
            return null;
        }

        var allDay = Str(start, "date") is not null;
        DateTimeOffset s, e;
        if (allDay)
        {
            if (!DateOnly.TryParse(Str(start, "date"), CultureInfo.InvariantCulture, out var sd)
                || !DateOnly.TryParse(Str(end, "date"), CultureInfo.InvariantCulture, out var ed))
            {
                return null;
            }

            s = LocalMidnight(sd);
            e = LocalMidnight(ed);
        }
        else if (!DateTimeOffset.TryParse(Str(start, "dateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out s)
                 || !DateTimeOffset.TryParse(Str(end, "dateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out e))
        {
            return null;
        }

        var title = Str(item, "summary");
        return new CalendarEvent(
            "g:" + calendar.Id + "|" + (Str(item, "id") ?? title) + "|" + s.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(title) ? "(no title)" : title.Trim(),
            s.ToLocalTime(),
            e < s ? s.ToLocalTime() : e.ToLocalTime(),
            allDay,
            string.IsNullOrWhiteSpace(Str(item, "location")) ? null : Str(item, "location")!.Trim(),
            calendar.Name,
            calendar.ColorHex);
    }

    private static DateTimeOffset LocalMidnight(DateOnly d)
    {
        var local = d.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                continue; // the access token expired early: refresh once
            }

            var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(ErrorOf(json) ?? $"HTTP {(int)response.StatusCode}");
            }

            return json;
        }

        throw new HttpRequestException("HTTP 401");
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpires)
            {
                return _accessToken;
            }

            var refresh = RefreshToken ?? throw new GoogleSignInExpiredException("not signed in");
            using var response = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId ?? string.Empty,
                ["client_secret"] = ClientSecret ?? string.Empty,
                ["refresh_token"] = refresh,
                ["grant_type"] = "refresh_token",
            }), cancellationToken).ConfigureAwait(false);
            var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = ErrorOf(json) ?? $"HTTP {(int)response.StatusCode}";
                if (Str(json, "error") is "invalid_grant" or "unauthorized_client" or "invalid_client")
                {
                    // Revoked, expired (apps in "Testing" lose sign-ins after 7 days) or client deleted.
                    _store.Set(RefreshTokenKey, null);
                    _accessToken = null;
                    throw new GoogleSignInExpiredException(error);
                }

                throw new HttpRequestException(error);
            }

            StoreAccessToken(json);
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void StoreAccessToken(JsonElement json)
    {
        _accessToken = Str(json, "access_token");
        var seconds = json.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var v) ? v : 3600;
        _accessTokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
            : [];

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? ErrorOf(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("error", out var error))
        {
            return null;
        }

        return error.ValueKind == JsonValueKind.String
            ? (Str(json, "error_description") is { } d ? $"{error.GetString()}: {d}" : error.GetString())
            : Str(error, "message");
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Encrypted for the current Windows user only (DPAPI).</summary>
    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    }

    private static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NexaDock.GoogleCalendar.v1");
}
