using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexaDock.Core.Usage;

namespace NexaDock.Infrastructure.Usage;

/// <summary>
/// Reads token usage from Claude Code's local session logs (<c>~/.claude/projects/**/*.jsonl</c>).
/// Only usage numbers, model names and timestamps are read; message text is never kept. Files are
/// read incrementally (only bytes appended since the last call), and entries older than
/// <see cref="Retention"/> are dropped.
/// </summary>
public sealed class ClaudeLogReader : IClaudeUsageSource
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(35);

    private readonly IReadOnlyList<string> _roots;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClaudeUsageEntry> _entries = new(StringComparer.Ordinal);

    public ClaudeLogReader(ILogger<ClaudeLogReader>? logger = null)
        : this(DefaultRoots(), logger, TimeProvider.System)
    {
    }

    public ClaudeLogReader(IReadOnlyList<string> roots, ILogger? logger, TimeProvider time)
    {
        _roots = roots;
        _logger = logger ?? NullLogger.Instance;
        _time = time;
    }

    public bool LogsFound => _roots.Any(Directory.Exists);

    /// <summary>Claude Code's config folders: CLAUDE_CONFIG_DIR (comma-separated), ~/.claude, ~/.config/claude.</summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>();
        var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            roots.AddRange(custom.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => Path.Combine(r, "projects")));
        }

        roots.Add(Path.Combine(home, ".claude", "projects"));
        roots.Add(Path.Combine(home, ".config", "claude", "projects"));
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<ClaudeUsageEntry> ReadRecent()
    {
        lock (_gate)
        {
            var cutoff = _time.GetUtcNow() - Retention;
            foreach (var root in _roots.Where(Directory.Exists))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Cannot list {Root}.", root);
                    continue;
                }

                foreach (var file in files)
                {
                    ReadNew(file, cutoff);
                }
            }

            foreach (var stale in _entries.Where(kv => kv.Value.Timestamp < cutoff).Select(kv => kv.Key).ToList())
            {
                _entries.Remove(stale);
            }

            return _entries.Values.OrderBy(e => e.Timestamp).ToList();
        }
    }

    private void ReadNew(string file, DateTimeOffset cutoff)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.LastWriteTimeUtc < cutoff.UtcDateTime)
            {
                _offsets[file] = info.Length; // old session: skip without parsing
                return;
            }

            var offset = _offsets.GetValueOrDefault(file);
            if (info.Length < offset)
            {
                offset = 0; // file was rewritten
            }

            if (info.Length == offset)
            {
                return;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - offset];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            // Only complete lines: a trailing line without '\n' may still be being written.
            var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (lastNewline < 0)
            {
                return;
            }

            var start = 0;
            for (var i = 0; i <= lastNewline; i++)
            {
                if (buffer[i] != (byte)'\n')
                {
                    continue;
                }

                var length = i - start;
                if (length > 0 && Contains(buffer.AsSpan(start, length), UsageMarker))
                {
                    var line = Encoding.UTF8.GetString(buffer, start, length).TrimEnd('\r');
                    if (Parse(line) is { } entry && entry.Timestamp >= cutoff)
                    {
                        _entries[entry.Key] = entry; // later lines carry the final usage numbers
                    }
                }

                start = i + 1;
            }

            _offsets[file] = offset + lastNewline + 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Cannot read {File}.", file);
        }
    }

    private static readonly byte[] UsageMarker = Encoding.UTF8.GetBytes("\"usage\"");

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle) >= 0;

    /// <summary>Extracts only usage fields from one log line; returns null for anything else.</summary>
    internal static ClaudeUsageEntry? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                return null;
            }

            static long Num(JsonElement obj, string name) =>
                obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? Math.Max(0, n) : 0;

            var model = message.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : string.Empty;
            if (model == "<synthetic>")
            {
                return null; // locally generated placeholder, not a real model call
            }

            var id = message.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String ? mid.GetString() : null;
            var request = root.TryGetProperty("requestId", out var rid) && rid.ValueKind == JsonValueKind.String ? rid.GetString() : null;
            var uuid = root.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            var key = id is not null ? $"{id}:{request}" : uuid ?? $"{timestamp.UtcTicks}";

            return new ClaudeUsageEntry(
                timestamp.ToUniversalTime(),
                model,
                Num(usage, "input_tokens"),
                Num(usage, "output_tokens"),
                Num(usage, "cache_creation_input_tokens"),
                Num(usage, "cache_read_input_tokens"),
                key);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
