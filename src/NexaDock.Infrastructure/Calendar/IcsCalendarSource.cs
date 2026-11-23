using System.Net.Http.Headers;
using Ical.Net;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Logging;
using NexaDock.Core;
using NexaDock.Core.Calendar;
using IcalCalendar = Ical.Net.Calendar;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace NexaDock.Infrastructure.Calendar;

/// <summary>
/// Downloads iCal feeds (for Google Calendar: the "secret address in iCal format") and expands
/// them into event occurrences, recurring events and time zones included. Read-only; the links
/// are never written to the log because they grant access to the calendar.
/// </summary>
public sealed class IcsCalendarSource : ICalendarSource
{
    private static readonly HttpClient Http = CreateClient();
    private readonly ILogger<IcsCalendarSource> _logger;

    public IcsCalendarSource(ILogger<IcsCalendarSource> logger) => _logger = logger;

    public async Task<CalendarFetchResult> FetchAsync(IReadOnlyList<string> feedUrls, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var events = new List<CalendarEvent>();
        var failed = 0;
        string? error = null;

        foreach (var url in feedUrls)
        {
            try
            {
                using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    failed++;
                    error = $"HTTP {(int)response.StatusCode}";
                    _logger.LogWarning("Calendar feed on {Host} returned {Status}.", CalendarAgenda.DescribeFeed(url), (int)response.StatusCode);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                events.AddRange(Parse(text, from, to));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                error = ex is HttpRequestException or TaskCanceledException ? "network" : "format";
                _logger.LogWarning("Calendar feed on {Host} could not be read: {Error}", CalendarAgenda.DescribeFeed(url), ex.GetType().Name + ": " + ex.Message);
            }
        }

        // The same event in two feeds (shared calendars) is shown once.
        var unique = events
            .GroupBy(e => e.Key)
            .Select(g => g.First())
            .OrderBy(e => e.Start)
            .ToList();
        return new CalendarFetchResult(unique, failed, error);
    }

    /// <summary>Expands an iCal document into occurrences that overlap [from, to).</summary>
    public static IReadOnlyList<CalendarEvent> Parse(string ics, DateTimeOffset from, DateTimeOffset to)
    {
        if (string.IsNullOrWhiteSpace(ics) || !ics.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Not an iCal document.");
        }

        var calendar = IcalCalendar.Load(ics) ?? throw new FormatException("Not an iCal document.");
        var result = new List<CalendarEvent>();

        // Start a day early so events already running at "from" are included, then filter.
        var start = new CalDateTime(from.UtcDateTime.AddDays(-1), CalDateTime.UtcTzId);
        var end = new CalDateTime(to.UtcDateTime, CalDateTime.UtcTzId);

        foreach (var occurrence in calendar.GetOccurrences<IcalEvent>(start).TakeWhileBefore(end))
        {
            if (occurrence.Source is not IcalEvent source
                || string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var period = occurrence.Period;
            var allDay = !period.StartTime.HasTime;
            var s = ToOffset(period.StartTime, allDay);
            var endTime = period.EffectiveEndTime;
            var e = endTime is null ? (allDay ? s.AddDays(1) : s) : ToOffset(endTime, allDay);
            if (e < s)
            {
                e = s;
            }

            // Keep what overlaps [from, to); a zero-length event counts at its start.
            var overlaps = e > from || (e == s && s >= from);
            if (!overlaps || s >= to)
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(source.Summary) ? "(no title)" : source.Summary.Trim();
            var location = string.IsNullOrWhiteSpace(source.Location) ? null : source.Location.Trim();
            var key = (source.Uid ?? title) + "|" + s.UtcDateTime.ToString("O");
            result.Add(new CalendarEvent(key, title, s, e, allDay, location));
        }

        return result;
    }

    /// <summary>
    /// Timed values become exact instants; all-day dates become local midnight of that date
    /// (a birthday is on the same calendar day wherever you are).
    /// </summary>
    private static DateTimeOffset ToOffset(CalDateTime value, bool allDay)
    {
        if (allDay || !value.HasTime)
        {
            var local = value.Value.Date;
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        if (value.IsFloating)
        {
            // No time zone in the feed: the wall-clock time is meant locally.
            var local = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        return new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc)).ToLocalTime();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductInfo.Name, ProductInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
        return client;
    }
}
