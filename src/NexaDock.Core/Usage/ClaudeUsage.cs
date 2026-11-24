namespace NexaDock.Core.Usage;

/// <summary>One model response recorded by Claude Code on this PC.</summary>
/// <param name="Key">Deduplication key (message id + request id): Claude Code logs a response in several lines.</param>
public sealed record ClaudeUsageEntry(
    DateTimeOffset Timestamp,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    string Key)
{
    /// <summary>New tokens processed (input + output + cache writes). Cache reads are reported separately.</summary>
    public long Tokens => InputTokens + OutputTokens + CacheCreationTokens;
}

/// <summary>A Claude usage session window (5 hours from the hour of its first message).</summary>
public sealed record UsageBlock(
    DateTimeOffset Start,
    DateTimeOffset End,
    DateTimeOffset LastActivity,
    long Tokens,
    long OutputTokens,
    long CacheReadTokens,
    int Messages,
    bool IsActive);

public sealed record DayUsage(DateOnly Day, long Tokens, int Messages);

public sealed record ModelUsage(string Model, long Tokens, int Messages);

public sealed record ClaudeUsageSummary(
    bool LogsFound,
    UsageBlock? ActiveBlock,
    long TodayTokens,
    int TodayMessages,
    long WeekTokens,
    IReadOnlyList<DayUsage> LastSevenDays,
    IReadOnlyList<ModelUsage> Models,
    long LargestBlockTokens,
    DateTimeOffset? LastActivity)
{
    /// <summary>Active block size relative to the busiest block of the last 30 days (0–100), or null.</summary>
    public double? ActiveShareOfRecord =>
        ActiveBlock is { } b && LargestBlockTokens > 0 ? Math.Min(100, 100.0 * b.Tokens / LargestBlockTokens) : null;

    public static ClaudeUsageSummary Empty(bool logsFound) =>
        new(logsFound, null, 0, 0, 0, [], [], 0, null);
}

/// <summary>Reads Claude Code usage from local logs (implemented in Infrastructure).</summary>
public interface IClaudeUsageSource
{
    /// <summary>True when at least one Claude Code log folder exists.</summary>
    bool LogsFound { get; }

    /// <summary>Recent entries (deduplicated), oldest first. Reads incrementally; may be slow on first call.</summary>
    IReadOnlyList<ClaudeUsageEntry> ReadRecent();
}

/// <summary>
/// Pure aggregation, mirroring how Claude usage windows work: a window opens with the first message
/// and resets exactly five hours later (first message 12:20 → resets at 17:20, as Claude itself
/// reports); a message after the reset starts a new window.
/// </summary>
public static class ClaudeUsageCalculator
{
    public static readonly TimeSpan BlockLength = TimeSpan.FromHours(5);

    public static IReadOnlyList<UsageBlock> Blocks(IEnumerable<ClaudeUsageEntry> entries, DateTimeOffset now)
    {
        var blocks = new List<UsageBlock>();
        DateTimeOffset start = default, end = default, last = default;
        long tokens = 0, output = 0, cacheRead = 0;
        var messages = 0;
        var open = false;

        void Close()
        {
            if (open)
            {
                blocks.Add(new UsageBlock(start, end, last, tokens, output, cacheRead, messages, now < end && now - last < BlockLength));
            }
        }

        foreach (var e in entries.OrderBy(e => e.Timestamp))
        {
            if (!open || e.Timestamp >= end)
            {
                Close();
                start = e.Timestamp.ToUniversalTime();
                end = ResetAfter(start);
                tokens = output = cacheRead = 0;
                messages = 0;
                open = true;
            }

            tokens += e.Tokens;
            output += e.OutputTokens;
            cacheRead += e.CacheReadTokens;
            messages++;
            last = e.Timestamp;
        }

        Close();
        return blocks;
    }

    /// <summary>Exactly five hours after the window opened.</summary>
    public static DateTimeOffset ResetAfter(DateTimeOffset firstMessage) =>
        firstMessage.ToUniversalTime() + BlockLength;

    public static ClaudeUsageSummary Summarize(IReadOnlyList<ClaudeUsageEntry> entries, DateTimeOffset now, TimeZoneInfo zone, bool logsFound = true)
    {
        if (entries.Count == 0)
        {
            return ClaudeUsageSummary.Empty(logsFound);
        }

        var blocks = Blocks(entries, now);
        var active = blocks.LastOrDefault(b => b.IsActive);
        var record = blocks.Where(b => b.Start >= now - TimeSpan.FromDays(30)).Select(b => b.Tokens).DefaultIfEmpty(0).Max();

        DateOnly LocalDay(DateTimeOffset t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zone).DateTime);
        var today = LocalDay(now);
        var firstDay = today.AddDays(-6);

        var byDay = entries
            .GroupBy(e => LocalDay(e.Timestamp))
            .ToDictionary(g => g.Key, g => (Tokens: g.Sum(e => e.Tokens), Messages: g.Count()));

        var days = Enumerable.Range(0, 7)
            .Select(i => firstDay.AddDays(i))
            .Select(d => byDay.TryGetValue(d, out var v) ? new DayUsage(d, v.Tokens, v.Messages) : new DayUsage(d, 0, 0))
            .ToList();

        var week = entries.Where(e => LocalDay(e.Timestamp) >= firstDay).ToList();
        var models = week
            .GroupBy(e => PrettyModel(e.Model))
            .Select(g => new ModelUsage(g.Key, g.Sum(e => e.Tokens), g.Count()))
            .OrderByDescending(m => m.Tokens)
            .ToList();

        var todayUsage = byDay.GetValueOrDefault(today);
        return new ClaudeUsageSummary(
            logsFound,
            active,
            todayUsage.Tokens,
            todayUsage.Messages,
            week.Sum(e => e.Tokens),
            days,
            models,
            record,
            entries.Max(e => e.Timestamp));
    }

    /// <summary>"claude-opus-5-5" → "Opus 5.5"; unknown names are returned unchanged.</summary>
    public static string PrettyModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "Unknown";
        }

        var parts = model.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "claude")
        {
            var family = char.ToUpperInvariant(parts[1][0]) + parts[1][1..];
            var version = string.Join('.', parts.Skip(2).TakeWhile(p => p.All(char.IsDigit) && p.Length <= 2));
            return version.Length > 0 ? $"{family} {version}" : family;
        }

        return model;
    }

    /// <summary>1 234 → "1.2K", 3 400 000 → "3.4M".</summary>
    public static string Compact(long value, System.Globalization.CultureInfo culture) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000.0).ToString("0.#", culture) + "B",
        >= 1_000_000 => (value / 1_000_000.0).ToString("0.#", culture) + "M",
        >= 1_000 => (value / 1_000.0).ToString("0.#", culture) + "K",
        _ => value.ToString(culture),
    };
}
