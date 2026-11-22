using NexaDock.Core.Models;

namespace NexaDock.Core.Settings;

/// <summary>Repairs out-of-range or inconsistent settings so the app can always start.</summary>
public static class SettingsNormalizer
{
    public static DockSettings Normalize(DockSettings? settings)
    {
        if (settings is null)
        {
            return DockSettings.Default;
        }

        var d = DockSettings.Default;

        return settings with
        {
            SchemaVersion = DockSettings.CurrentSchemaVersion,
            Edge = Enum.IsDefined(settings.Edge) ? settings.Edge : d.Edge,
            Size = Enum.IsDefined(settings.Size) ? settings.Size : d.Size,
            Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : d.Theme,
            MonitorDeviceName = string.IsNullOrWhiteSpace(settings.MonitorDeviceName) ? null : settings.MonitorDeviceName.Trim(),
            Width = Clamp(settings.Width, SettingsLimits.MinWidth, SettingsLimits.MaxWidth, d.Width),
            Spacing = Clamp(settings.Spacing, SettingsLimits.MinSpacing, SettingsLimits.MaxSpacing, d.Spacing),
            Opacity = Clamp(settings.Opacity, SettingsLimits.MinOpacity, SettingsLimits.MaxOpacity, d.Opacity),
            EdgeOffset = Clamp(settings.EdgeOffset, SettingsLimits.MinEdgeOffset, SettingsLimits.MaxEdgeOffset, d.EdgeOffset),
            Shortcuts = NormalizeShortcuts(settings.Shortcuts),
        };
    }

    internal static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static IReadOnlyList<DockShortcut> NormalizeShortcuts(IReadOnlyList<DockShortcut>? shortcuts)
    {
        if (shortcuts is null || shortcuts.Count == 0)
        {
            return Array.Empty<DockShortcut>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DockShortcut>(shortcuts.Count);

        foreach (var s in shortcuts)
        {
            if (s is null || string.IsNullOrWhiteSpace(s.Target))
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(s.Id) || !seen.Add(s.Id) ? NewUniqueId(seen) : s.Id;
            var name = string.IsNullOrWhiteSpace(s.Name) ? DeriveName(s.Target) : s.Name.Trim();

            result.Add(s with
            {
                Id = id,
                Name = name,
                Target = s.Target.Trim(),
                Arguments = string.IsNullOrWhiteSpace(s.Arguments) ? null : s.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(s.WorkingDirectory) ? null : s.WorkingDirectory,
            });

            if (result.Count >= SettingsLimits.MaxShortcuts)
            {
                break;
            }
        }

        return result;
    }

    private static string NewUniqueId(HashSet<string> seen)
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N");
        }
        while (!seen.Add(id));
        return id;
    }

    internal static string DeriveName(string target)
    {
        var trimmed = target.Trim().TrimEnd('\\', '/');
        var slash = trimmed.LastIndexOfAny(['\\', '/']);
        var file = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        var dot = file.LastIndexOf('.');
        var name = dot > 0 ? file[..dot] : file;
        return string.IsNullOrWhiteSpace(name) ? "Shortcut" : name;
    }
}
