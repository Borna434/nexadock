using System.Text.Json;
using System.Text.Json.Serialization;
using NexaDock.Core.Models;

namespace NexaDock.Core.Settings;

/// <summary>
/// JSON (de)serialization for settings. Reading is tolerant: each field is parsed independently, so a
/// single bad value falls back to its default instead of discarding the whole configuration.
/// </summary>
public static class SettingsSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(DockSettings settings) =>
        JsonSerializer.Serialize(SettingsNormalizer.Normalize(settings), WriteOptions);

    /// <summary>Parses JSON; returns <c>null</c> when the document itself is not valid JSON or not an object.</summary>
    public static DockSettings? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var d = DockSettings.Default;
            var result = new DockSettings
            {
                SchemaVersion = ReadInt(root, nameof(DockSettings.SchemaVersion)) ?? d.SchemaVersion,
                Edge = ReadEnum(root, nameof(DockSettings.Edge), d.Edge),
                MonitorDeviceName = ReadString(root, nameof(DockSettings.MonitorDeviceName)),
                Size = ReadEnum(root, nameof(DockSettings.Size), d.Size),
                Theme = ReadEnum(root, nameof(DockSettings.Theme), d.Theme),
                Width = ReadDouble(root, nameof(DockSettings.Width)) ?? d.Width,
                Spacing = ReadDouble(root, nameof(DockSettings.Spacing)) ?? d.Spacing,
                Opacity = ReadDouble(root, nameof(DockSettings.Opacity)) ?? d.Opacity,
                EdgeOffset = ReadDouble(root, nameof(DockSettings.EdgeOffset)) ?? d.EdgeOffset,
                ReducedMotion = ReadBool(root, nameof(DockSettings.ReducedMotion)) ?? d.ReducedMotion,
                ShortcutsInitialized = ReadBool(root, nameof(DockSettings.ShortcutsInitialized)) ?? d.ShortcutsInitialized,
                Shortcuts = ReadShortcuts(root),
            };

            return SettingsNormalizer.Normalize(result);
        }
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int? ReadInt(JsonElement root, string name) =>
        TryGet(root, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? ReadDouble(JsonElement root, string name) =>
        TryGet(root, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : null;

    private static bool? ReadBool(JsonElement root, string name) =>
        TryGet(root, name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static string? ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static T ReadEnum<T>(JsonElement root, string name, T fallback)
        where T : struct, Enum
    {
        if (!TryGet(root, name, out var v))
        {
            return fallback;
        }

        if (v.ValueKind == JsonValueKind.String
            && Enum.TryParse<T>(v.GetString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            && !int.TryParse(v.GetString(), out _))
        {
            return parsed;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
        {
            var candidate = (T)Enum.ToObject(typeof(T), n);
            if (Enum.IsDefined(candidate))
            {
                return candidate;
            }
        }

        return fallback;
    }

    private static IReadOnlyList<DockShortcut> ReadShortcuts(JsonElement root)
    {
        if (!TryGet(root, nameof(DockSettings.Shortcuts), out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DockShortcut>();
        }

        var list = new List<DockShortcut>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var target = ReadString(item, nameof(DockShortcut.Target));
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            list.Add(new DockShortcut(
                ReadString(item, nameof(DockShortcut.Id)) ?? string.Empty,
                ReadString(item, nameof(DockShortcut.Name)) ?? string.Empty,
                target,
                ReadString(item, nameof(DockShortcut.Arguments)),
                ReadString(item, nameof(DockShortcut.WorkingDirectory))));
        }

        return list;
    }
}
