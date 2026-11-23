using System.Text.Json;
using System.Text.Json.Nodes;
using NexaDock.Core.Models;

namespace NexaDock.Core.Settings;

/// <summary>What a backup file holds, ready to apply.</summary>
public sealed record BackupContents(DockSettings Settings, IReadOnlyDictionary<string, string> Data, DateTimeOffset Created);

/// <summary>
/// A portable copy of the bar: layout, profiles, pinned apps, look, and widget content (to-dos,
/// notes, calendar links). Sign-ins are left out: they are encrypted for this Windows account and
/// would not work elsewhere, so you sign in again after a restore.
/// </summary>
public static class SettingsBackup
{
    public const string Format = "nexadock-backup";
    public const int Version = 1;

    /// <summary>Data keys that never leave this PC.</summary>
    public static bool IsPrivate(string key) =>
        key.StartsWith("google.", StringComparison.OrdinalIgnoreCase);

    public static string Create(DockSettings settings, IReadOnlyDictionary<string, string> data, DateTimeOffset now)
    {
        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["created"] = now.ToString("O"),
            ["app"] = ProductInfo.DisplayTitle,
            ["settings"] = JsonNode.Parse(SettingsSerializer.Serialize(settings)),
        };

        var dataNode = new JsonObject();
        foreach (var (key, value) in data.Where(d => !IsPrivate(d.Key)).OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            dataNode[key] = value;
        }

        root["data"] = dataNode;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads a backup; throws <see cref="FormatException"/> when it is not one.</summary>
    /// <param name="current">Machine-specific values (startup, saved taskbar state) are kept from here.</param>
    public static BackupContents Parse(string json, DockSettings current)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Not a NexaDock backup.", ex);
        }

        if (root is not JsonObject obj || (string?)obj["format"] != Format)
        {
            throw new FormatException("Not a NexaDock backup.");
        }

        if ((int?)obj["version"] is not { } version || version > Version)
        {
            throw new FormatException("This backup was made by a newer NexaDock.");
        }

        var settings = SettingsSerializer.Deserialize(obj["settings"]?.ToJsonString())
            ?? throw new FormatException("The backup has no settings.");

        settings = settings with
        {
            LaunchAtStartup = current.LaunchAtStartup,
            TaskbarStateBeforeReplace = current.TaskbarStateBeforeReplace,
        };

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        if (obj["data"] is JsonObject dataNode)
        {
            foreach (var (key, value) in dataNode)
            {
                if (!IsPrivate(key) && value is JsonValue v && v.TryGetValue<string>(out var text))
                {
                    data[key] = text;
                }
            }
        }

        var created = DateTimeOffset.TryParse((string?)obj["created"], out var c) ? c : DateTimeOffset.MinValue;
        return new BackupContents(SettingsNormalizer.Normalize(settings), data, created);
    }
}
