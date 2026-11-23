using System.Text.Json;
using NexaDock.Core.Launcher;

namespace NexaDock.Core.Clipboard;

public enum ClipKind
{
    Text,
    Image,
}

/// <summary>Something that was copied. Images keep their PNG bytes (in memory only).</summary>
public sealed record ClipEntry(string Id, ClipKind Kind, string? Text, byte[]? Png, int Width, int Height, DateTimeOffset Copied, bool Pinned)
{
    public static ClipEntry FromText(string text, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N"), ClipKind.Text, text, null, 0, 0, now, false);

    public static ClipEntry FromImage(byte[] png, int width, int height, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N"), ClipKind.Image, null, png, width, height, now, false);
}

/// <summary>
/// The list behind the clipboard history: newest first, pinned items kept, duplicates moved to the
/// top instead of repeated, and a cap on how much is remembered. Only text (and pins) is saved to
/// disk; images live in memory for this session.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxItems = 30;
    public const int MaxImages = 8;
    public const int MaxTextLength = 20_000;

    private readonly List<ClipEntry> _items = [];

    public IReadOnlyList<ClipEntry> Items => _items;

    /// <summary>Adds a copy; returns false when it was ignored (empty, too large, same as the newest).</summary>
    public bool Add(ClipEntry entry)
    {
        if (entry.Kind == ClipKind.Text)
        {
            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                return false;
            }

            if (entry.Text.Length > MaxTextLength)
            {
                entry = entry with { Text = entry.Text[..MaxTextLength] };
            }

            var existing = _items.FindIndex(i => i.Kind == ClipKind.Text && i.Text == entry.Text);
            if (existing == 0)
            {
                return false;
            }

            if (existing > 0)
            {
                // Copied again: move it to the top, keep its pin.
                var old = _items[existing];
                _items.RemoveAt(existing);
                _items.Insert(0, old with { Copied = entry.Copied });
                return true;
            }
        }
        else if (entry.Png is null || entry.Png.Length == 0)
        {
            return false;
        }
        else if (_items.Count > 0 && _items[0].Kind == ClipKind.Image && _items[0].Png!.AsSpan().SequenceEqual(entry.Png))
        {
            return false;
        }

        _items.Insert(0, entry);
        Trim();
        return true;
    }

    public void Remove(string id) => _items.RemoveAll(i => i.Id == id);

    public void TogglePin(string id)
    {
        var index = _items.FindIndex(i => i.Id == id);
        if (index >= 0)
        {
            _items[index] = _items[index] with { Pinned = !_items[index].Pinned };
        }
    }

    /// <summary>Clears everything except pinned items.</summary>
    public void ClearUnpinned() => _items.RemoveAll(i => !i.Pinned);

    /// <summary>Pinned first (in their order), then the rest by time; filtered by the query.</summary>
    public IReadOnlyList<ClipEntry> View(string? query)
    {
        var q = query?.Trim() ?? string.Empty;
        var list = _items.OrderByDescending(i => i.Pinned).ToList();
        if (q.Length == 0)
        {
            return list;
        }

        var needle = FuzzyMatcher.Normalize(q);
        return list.Where(i => i.Kind == ClipKind.Text && FuzzyMatcher.Normalize(i.Text!).Contains(needle, StringComparison.Ordinal)).ToList();
    }

    private void Trim()
    {
        var images = 0;
        var kept = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.Pinned)
            {
                continue;
            }

            if (item.Kind == ClipKind.Image && ++images > MaxImages || ++kept > MaxItems)
            {
                _items.RemoveAt(i--);
            }
        }
    }

    // ---- Saving (text only) ---------------------------------------------------------------------

    private sealed record Saved(string Id, string Text, DateTimeOffset Copied, bool Pinned);

    public string Serialize() =>
        JsonSerializer.Serialize(_items.Where(i => i.Kind == ClipKind.Text)
            .Select(i => new Saved(i.Id, i.Text!, i.Copied, i.Pinned)).ToList());

    public void Load(string? json)
    {
        _items.Clear();
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            foreach (var s in JsonSerializer.Deserialize<List<Saved>>(json) ?? [])
            {
                if (!string.IsNullOrEmpty(s.Text))
                {
                    _items.Add(new ClipEntry(s.Id, ClipKind.Text, s.Text, null, 0, 0, s.Copied, s.Pinned));
                }
            }
        }
        catch (JsonException)
        {
            _items.Clear();
        }
    }
}
