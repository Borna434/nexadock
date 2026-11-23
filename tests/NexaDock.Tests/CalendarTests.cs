using NexaDock.Core.Calendar;
using NexaDock.Infrastructure.Calendar;

namespace NexaDock.Tests;

public class CalendarTests
{
    // Google-style feed: a weekly meeting in Europe/Zagreb with one skipped week, an all-day event,
    // a UTC event, a cancelled event and an event with no title.
    private const string Feed = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Google Inc//Google Calendar 70.9054//EN
        BEGIN:VTIMEZONE
        TZID:Europe/Zagreb
        BEGIN:DAYLIGHT
        TZOFFSETFROM:+0100
        TZOFFSETTO:+0200
        TZNAME:CEST
        DTSTART:19700329T020000
        RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
        END:DAYLIGHT
        BEGIN:STANDARD
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0100
        TZNAME:CET
        DTSTART:19701025T030000
        RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
        END:STANDARD
        END:VTIMEZONE
        BEGIN:VEVENT
        UID:weekly@example
        SUMMARY:Team sync
        LOCATION:Room 1
        DTSTART;TZID=Europe/Zagreb:20261102T100000
        DTEND;TZID=Europe/Zagreb:20261102T103000
        RRULE:FREQ=WEEKLY;BYDAY=MO
        EXDATE;TZID=Europe/Zagreb:20261116T100000
        END:VEVENT
        BEGIN:VEVENT
        UID:allday@example
        SUMMARY:Holiday
        DTSTART;VALUE=DATE:20261125
        DTEND;VALUE=DATE:20261126
        END:VEVENT
        BEGIN:VEVENT
        UID:utc@example
        SUMMARY:Call
        DTSTART:20261124T150000Z
        DTEND:20261124T160000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelled@example
        SUMMARY:Cancelled thing
        STATUS:CANCELLED
        DTSTART:20261124T170000Z
        DTEND:20261124T180000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:untitled@example
        DTSTART:20261126T080000Z
        DTEND:20261126T090000Z
        END:VEVENT
        END:VCALENDAR
        """;

    private static readonly DateTimeOffset From = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recurring_events_are_expanded_in_their_time_zone_and_skip_exdates()
    {
        var events = IcsCalendarSource.Parse(Feed, From, To);
        var sync = events.Where(e => e.Title == "Team sync").Select(e => e.Start.UtcDateTime).ToList();

        // Mondays 10:00 CET = 09:00 UTC; 16 Nov is excluded.
        Assert.Equal(
            [new DateTime(2026, 11, 9, 9, 0, 0), new DateTime(2026, 11, 23, 9, 0, 0), new DateTime(2026, 11, 30, 9, 0, 0)],
            sync);
        Assert.All(events.Where(e => e.Title == "Team sync"), e => Assert.Equal(TimeSpan.FromMinutes(30), e.End - e.Start));
        Assert.Equal("Room 1", events.First(e => e.Title == "Team sync").Location);
    }

    [Fact]
    public void All_day_cancelled_and_untitled_events_are_handled()
    {
        var events = IcsCalendarSource.Parse(Feed, From, To);

        var holiday = Assert.Single(events, e => e.Title == "Holiday");
        Assert.True(holiday.AllDay);
        Assert.Equal(new DateTime(2026, 11, 25), holiday.Start.LocalDateTime.Date);
        Assert.Equal(TimeSpan.FromDays(1), holiday.End - holiday.Start);

        Assert.DoesNotContain(events, e => e.Title == "Cancelled thing");
        Assert.Contains(events, e => e.Title == "(no title)");
        Assert.Equal(events.Count, events.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.Throws<FormatException>(() => IcsCalendarSource.Parse("<html>Sign in</html>", From, To));
    }

    [Theory]
    [InlineData("https://calendar.google.com/calendar/ical/x%40gmail.com/private-abc/basic.ics", true)]
    [InlineData("webcal://example.com/cal.ics", true)]
    [InlineData("  https://example.com/a.ics  ", true)]
    [InlineData("http://example.com/a.ics", false)]
    [InlineData("file:///C:/a.ics", false)]
    [InlineData("not a link", false)]
    [InlineData("", false)]
    public void Only_https_and_webcal_links_are_accepted(string text, bool ok)
    {
        Assert.Equal(ok, CalendarAgenda.TryNormalizeFeedUrl(text, out var url));
        if (ok)
        {
            Assert.StartsWith("https://", url);
        }
    }

    [Fact]
    public void Google_public_address_is_recognized()
    {
        Assert.True(CalendarAgenda.IsGooglePublicAddress("https://calendar.google.com/calendar/ical/a%40gmail.com/public/basic.ics"));
        Assert.False(CalendarAgenda.IsGooglePublicAddress("https://calendar.google.com/calendar/ical/a%40gmail.com/private-123/basic.ics"));
    }

    private static CalendarEvent Ev(string title, DateTimeOffset start, TimeSpan length, bool allDay = false) =>
        new(title + start.ToString("O"), title, start, start + length, allDay, null);

    [Fact]
    public void Next_is_the_running_or_next_timed_event()
    {
        var now = new DateTimeOffset(2026, 11, 23, 10, 15, 0, TimeSpan.Zero);
        var events = new[]
        {
            Ev("Past", now.AddHours(-2), TimeSpan.FromHours(1)),
            Ev("Running", now.AddMinutes(-15), TimeSpan.FromMinutes(30)),
            Ev("Later", now.AddHours(1), TimeSpan.FromHours(1)),
            Ev("All day", now.Date, TimeSpan.FromDays(1), allDay: true),
        };

        Assert.Equal("Running", CalendarAgenda.Next(events, now)?.Title);
        Assert.Equal("Later", CalendarAgenda.Next(events, now.AddMinutes(20))?.Title);
        Assert.Null(CalendarAgenda.Next(events, now.AddHours(3)));
    }

    [Fact]
    public void Days_with_events_treat_the_end_as_exclusive()
    {
        var zone = TimeZoneInfo.Utc;
        var events = new[]
        {
            Ev("Two days", new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(2), allDay: true),
            Ev("Evening", new DateTimeOffset(2026, 11, 28, 22, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(2)),
        };

        var days = CalendarAgenda.DaysWithEvents(events, zone);

        Assert.Equal([new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 28)], days.Order());
    }

    [Fact]
    public void Upcoming_groups_by_day_and_hides_finished_events()
    {
        var zone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Ev("Morning", new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1)),
            Ev("Afternoon", new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1)),
            Ev("Holiday", new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1), allDay: true),
            Ev("Far away", new DateTimeOffset(2026, 12, 20, 9, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1)),
        };

        var upcoming = CalendarAgenda.Upcoming(events, now, zone, days: 7, maxEvents: 10);

        Assert.Equal(2, upcoming.Count);
        Assert.Equal("Afternoon", Assert.Single(upcoming[0].Events).Title);
        Assert.Equal(new DateOnly(2026, 11, 25), upcoming[1].Day);
    }
}
