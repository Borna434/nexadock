using System.Text.Json;
using NexaDock.Core.Calendar;
using NexaDock.Infrastructure.Calendar;

namespace NexaDock.Tests;

public class GoogleCalendarTests
{
    private static readonly GoogleCalendarInfo Exams = new("exams@group.calendar.google.com", "Faks – ispiti", "#d50000", true, false);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void Timed_event_keeps_its_instant_and_gets_the_calendar_colour()
    {
        var e = GoogleCalendarClient.ToEvent(Json("""
            {"id":"a1","status":"confirmed","summary":" Kolokvij ","location":"Dvorana 3",
             "start":{"dateTime":"2026-11-24T10:00:00+01:00"},"end":{"dateTime":"2026-11-24T12:00:00+01:00"}}
            """), Exams)!;

        Assert.Equal("Kolokvij", e.Title);
        Assert.Equal(new DateTime(2026, 11, 24, 9, 0, 0), e.Start.UtcDateTime);
        Assert.Equal(TimeSpan.FromHours(2), e.End - e.Start);
        Assert.False(e.AllDay);
        Assert.Equal("Dvorana 3", e.Location);
        Assert.Equal("Faks – ispiti", e.CalendarName);
        Assert.Equal("#d50000", e.ColorHex);
    }

    [Fact]
    public void All_day_event_spans_local_days_with_exclusive_end()
    {
        var e = GoogleCalendarClient.ToEvent(Json("""
            {"id":"b","summary":"Rok","start":{"date":"2026-11-25"},"end":{"date":"2026-11-27"}}
            """), Exams)!;

        Assert.True(e.AllDay);
        Assert.Equal(new DateTime(2026, 11, 25), e.Start.LocalDateTime);
        Assert.Equal(TimeSpan.FromDays(2), e.End - e.Start);
    }

    [Theory]
    [InlineData("""{"id":"c","status":"cancelled","start":{"date":"2026-11-25"},"end":{"date":"2026-11-26"}}""")]
    [InlineData("""{"id":"d","summary":"no times"}""")]
    [InlineData("""{"id":"e","start":{"dateTime":"nonsense"},"end":{"dateTime":"2026-11-24T12:00:00Z"}}""")]
    public void Cancelled_or_malformed_events_are_skipped(string json)
    {
        Assert.Null(GoogleCalendarClient.ToEvent(Json(json), Exams));
    }

    [Fact]
    public void Untitled_events_get_a_placeholder_and_keys_differ_per_calendar()
    {
        var json = Json("""{"id":"x","start":{"dateTime":"2026-11-24T10:00:00Z"},"end":{"dateTime":"2026-11-24T11:00:00Z"}}""");
        var a = GoogleCalendarClient.ToEvent(json, Exams)!;
        var b = GoogleCalendarClient.ToEvent(json, Exams with { Id = "other" })!;

        Assert.Equal("(no title)", a.Title);
        Assert.NotEqual(a.Key, b.Key);
    }

    [Fact]
    public void Ical_feeds_report_their_own_name()
    {
        Assert.Equal("Faks – nastava, ljeto", IcsCalendarSource.CalendarName("BEGIN:VCALENDAR\r\nX-WR-CALNAME:Faks – nastava\\, ljeto\r\nBEGIN:VEVENT\r\nEND:VEVENT\r\n"));
        Assert.Null(IcsCalendarSource.CalendarName("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nX-WR-CALNAME:inside\r\nEND:VEVENT\r\n"));
    }
}
