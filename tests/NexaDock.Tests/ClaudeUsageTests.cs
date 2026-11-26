using System.Globalization;
using NexaDock.Core.Usage;
using NexaDock.Infrastructure.Usage;

namespace NexaDock.Tests;

public class ClaudeUsageCalculatorTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);

    private static ClaudeUsageEntry E(double hours, long tokens = 100, string model = "claude-opus-5-5") =>
        new(Day.AddSeconds(Math.Round(hours * 3600)), model, tokens / 2, tokens / 2, 0, 1000, Guid.NewGuid().ToString());

    [Fact]
    public void Window_resets_exactly_five_hours_after_the_first_message()
    {
        // As Claude reports it: first message 12:20 → "resets at 17:20".
        var blocks = ClaudeUsageCalculator.Blocks([E(12 + 20 / 60.0), E(14), E(17.3)], Day.AddHours(17.3));

        var b = Assert.Single(blocks);
        Assert.Equal(Day.AddHours(12).AddMinutes(20), b.Start);
        Assert.Equal(Day.AddHours(17).AddMinutes(20), b.End);
        Assert.Equal(3, b.Messages);
        Assert.True(b.IsActive); // 17:18 is still before the 17:20 reset
    }

    [Fact]
    public void Reset_is_not_rounded()
    {
        Assert.Equal(Day.AddHours(14), ClaudeUsageCalculator.ResetAfter(Day.AddHours(9)));
        Assert.Equal(Day.AddHours(14.5), ClaudeUsageCalculator.ResetAfter(Day.AddHours(9.5)));
    }

    [Fact]
    public void Message_after_the_window_starts_a_new_block()
    {
        // First window 09:30 → resets 14:30; 14:10 still belongs to it, 14:40 opens the next one.
        var blocks = ClaudeUsageCalculator.Blocks([E(9.5), E(14 + 10 / 60.0), E(14 + 40 / 60.0)], Day.AddHours(15.2));

        Assert.Equal(2, blocks.Count);
        Assert.Equal(2, blocks[0].Messages);
        Assert.Equal(Day.AddHours(14).AddMinutes(40), blocks[1].Start);
        Assert.Equal(Day.AddHours(19).AddMinutes(40), blocks[1].End);
        Assert.True(blocks[1].IsActive);
    }

    [Fact]
    public void Summary_reports_active_block_today_week_models_and_record()
    {
        var entries = new[]
        {
            E(-48, 5000, "claude-sonnet-5-5"), // two days ago, big block (the record)
            E(-47, 5000, "claude-sonnet-5-5"),
            E(10, 1000), E(11, 1000), E(12, 500),
        };

        var s = ClaudeUsageCalculator.Summarize(entries, Day.AddHours(12.5), TimeZoneInfo.Utc);

        Assert.NotNull(s.ActiveBlock);
        Assert.Equal(2500, s.ActiveBlock!.Tokens);
        Assert.Equal(10000, s.LargestBlockTokens);
        Assert.Equal(25, s.ActiveShareOfRecord);
        Assert.Equal(2500, s.TodayTokens);
        Assert.Equal(3, s.TodayMessages);
        Assert.Equal(12500, s.WeekTokens);
        Assert.Equal(7, s.LastSevenDays.Count);
        Assert.Equal(DateOnly.FromDateTime(Day.DateTime), s.LastSevenDays[^1].Day);
        Assert.Equal("Sonnet 5.5", s.Models[0].Model);
    }

    [Fact]
    public void No_entries_gives_an_empty_summary() =>
        Assert.Null(ClaudeUsageCalculator.Summarize([], Day, TimeZoneInfo.Utc).ActiveBlock);

    [Theory]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-haiku", "Haiku")]
    [InlineData("gpt-x", "gpt-x")]
    public void Model_names_are_prettified(string raw, string expected) =>
        Assert.Equal(expected, ClaudeUsageCalculator.PrettyModel(raw));

    [Fact]
    public void Compact_numbers() =>
        Assert.Equal(["950", "1.2K", "3.4M"], new[] { 950L, 1234, 3_400_000 }.Select(v => ClaudeUsageCalculator.Compact(v, CultureInfo.InvariantCulture)));
}

public sealed class ClaudeLogReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nexadock-tests", Guid.NewGuid().ToString("N"), "projects");
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));

    public ClaudeLogReaderTests() => Directory.CreateDirectory(Path.Combine(_root, "proj"));

    private static string Line(string id, string request, int output, string time = "2026-11-23T10:00:00.000Z") =>
        "{\"type\":\"assistant\",\"requestId\":\"" + request + "\",\"timestamp\":\"" + time + "\",\"message\":{\"id\":\"" + id
        + "\",\"model\":\"claude-opus-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"secret text\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":"
        + output + ",\"cache_creation_input_tokens\":5,\"cache_read_input_tokens\":1000}}}";

    [Fact]
    public void Reads_usage_deduplicates_and_ignores_other_lines()
    {
        var file = Path.Combine(_root, "proj", "s.jsonl");
        File.WriteAllText(file,
            """{"type":"user","message":{"role":"user","content":"hello"}}""" + "\n" +
            Line("m1", "r1", 3) + "\n" +
            Line("m1", "r1", 40) + "\n" + // same response logged again with final numbers
            Line("m2", "r2", 7) + "\n" +
            "not json\n");

        var reader = new ClaudeLogReader([_root], null, _time);
        var entries = reader.ReadRecent();

        Assert.Equal(2, entries.Count);
        Assert.Equal(40, entries.Single(e => e.Key.StartsWith("m1", StringComparison.Ordinal)).OutputTokens);
        Assert.Equal(55, entries.Single(e => e.Key.StartsWith("m1", StringComparison.Ordinal)).Tokens);
        Assert.True(reader.LogsFound);
    }

    [Fact]
    public void Reads_incrementally_and_waits_for_complete_lines()
    {
        var file = Path.Combine(_root, "proj", "s.jsonl");
        File.WriteAllText(file, Line("m1", "r1", 1) + "\n" + Line("m2", "r2", 2)); // second line unfinished

        var reader = new ClaudeLogReader([_root], null, _time);
        Assert.Single(reader.ReadRecent());

        File.AppendAllText(file, "\n" + Line("m3", "r3", 3) + "\n");
        Assert.Equal(3, reader.ReadRecent().Count);
    }

    [Fact]
    public void Old_entries_are_dropped()
    {
        File.WriteAllText(Path.Combine(_root, "proj", "s.jsonl"),
            Line("old", "r", 1, "2026-09-01T10:00:00.000Z") + "\n" + Line("new", "r", 1) + "\n");

        var entries = new ClaudeLogReader([_root], null, _time).ReadRecent();
        Assert.Equal("new:r", Assert.Single(entries).Key);
    }

    [Fact]
    public void Missing_folders_are_fine()
    {
        var reader = new ClaudeLogReader([Path.Combine(_root, "nope")], null, _time);
        Assert.False(reader.LogsFound);
        Assert.Empty(reader.ReadRecent());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class ClaudeLimitsClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_five_hour_and_weekly_percentages()
    {
        var limits = ClaudeLimitsClient.Parse(
            """{"five_hour":{"utilization":6.0,"resets_at":"2026-11-26T23:50:00+00:00"},"seven_day":{"utilization":77,"resets_at":"2026-11-28T09:00:00Z"},"seven_day_opus":null}""",
            Now);

        Assert.NotNull(limits);
        Assert.Equal(6, limits!.FiveHour!.Percent);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 23, 50, 0, TimeSpan.Zero), limits.FiveHour.ResetsAt);
        Assert.Equal(77, limits.Weekly!.Percent);
    }

    [Fact]
    public void Missing_limits_give_null()
    {
        Assert.Null(ClaudeLimitsClient.Parse("""{"five_hour":null,"seven_day":null}""", Now));
        Assert.Null(ClaudeLimitsClient.Parse("""{"error":{"type":"authentication_error"}}""", Now));
    }
}
