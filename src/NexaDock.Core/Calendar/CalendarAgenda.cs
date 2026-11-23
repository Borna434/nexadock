namespace NexaDock.Core.Calendar;

/// <summary>One occurrence of a calendar event (recurring events are already expanded).</summary>
/// <param name="Key">Stable identity of this occurrence (event UID plus its start).</param>
/// <param name="AllDay">All-day events start at local midnight; <paramref name="End"/> is exclusive.</param>
public sealed record CalendarEvent(
    string Key,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay,
    string? Location);

/// <param name="FailedFeeds">How many feeds could not be downloaded or read.</param>
public sealed record CalendarFetchResult(IReadOnlyList<CalendarEvent> Events, int FailedFeeds, string? Error);

/// <summary>Reads events from calendar feeds (iCal / .ics links such as Google Calendar's secret address).</summary>
public interface ICalendarSource
{
    Task<CalendarFetchResult> FetchAsync(IReadOnlyList<string> feedUrls, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Pure agenda logic, independent of where the events come from.</summary>
public static class CalendarAgenda
{
    /// <summary>
    /// Accepts an https or webcal link to an iCal feed and returns it in a form HttpClient can fetch.
    /// </summary>
    public static bool TryNormalizeFeedUrl(string? text, out string url)
    {
        url = string.Empty;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed["webcal://".Length..];
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    /// <summary>A Google Calendar feed that is the public address rather than the secret one.</summary>
    public static bool IsGooglePublicAddress(string url) =>
        url.Contains("calendar.google.com", StringComparison.OrdinalIgnoreCase)
        && url.Contains("/public/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Host only, for logs: the full link of a private feed is a secret.</summary>
    public static string DescribeFeed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(invalid)";

    /// <summary>The event in progress or the next one to start; all-day events are skipped.</summary>
    public static CalendarEvent? Next(IEnumerable<CalendarEvent> events, DateTimeOffset now) =>
        events.Where(e => !e.AllDay && e.End > now)
              .OrderBy(e => e.Start)
              .ThenBy(e => e.Title, StringComparer.CurrentCulture)
              .FirstOrDefault();

    /// <summary>Events that touch the given local day, all-day events first.</summary>
    public static IReadOnlyList<CalendarEvent> ForDay(IEnumerable<CalendarEvent> events, DateOnly day, TimeZoneInfo zone)
    {
        var (start, end) = DayBounds(day, zone);
        return events.Where(e => e.Start < end && e.End > start)
                     .OrderByDescending(e => e.AllDay)
                     .ThenBy(e => e.Start)
                     .ThenBy(e => e.Title, StringComparer.CurrentCulture)
                     .ToList();
    }

    /// <summary>Local days that have at least one event (for dots in the month grid).</summary>
    public static IReadOnlySet<DateOnly> DaysWithEvents(IEnumerable<CalendarEvent> events, TimeZoneInfo zone)
    {
        var days = new HashSet<DateOnly>();
        foreach (var e in events)
        {
            var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Start, zone).DateTime);
            // End is exclusive: an event ending exactly at midnight does not mark the next day.
            var lastMoment = e.End > e.Start ? e.End.AddTicks(-1) : e.Start;
            var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(lastMoment, zone).DateTime);
            for (var d = first; d <= last && days.Count < 2000; d = d.AddDays(1))
            {
                days.Add(d);
            }
        }

        return days;
    }

    /// <summary>The upcoming days (starting today) that have events, each with its events.</summary>
    public static IReadOnlyList<(DateOnly Day, IReadOnlyList<CalendarEvent> Events)> Upcoming(
        IReadOnlyList<CalendarEvent> events, DateTimeOffset now, TimeZoneInfo zone, int days, int maxEvents)
    {
        var result = new List<(DateOnly, IReadOnlyList<CalendarEvent>)>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var shown = 0;
        for (var i = 0; i < days && shown < maxEvents; i++)
        {
            var day = today.AddDays(i);
            var list = ForDay(events, day, zone)
                .Where(e => e.End > now)
                .Take(maxEvents - shown)
                .ToList();
            if (list.Count > 0)
            {
                result.Add((day, list));
                shown += list.Count;
            }
        }

        return result;
    }

    public static (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly day, TimeZoneInfo zone)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (new DateTimeOffset(start, zone.GetUtcOffset(start)), new DateTimeOffset(end, zone.GetUtcOffset(end)));
    }
}
