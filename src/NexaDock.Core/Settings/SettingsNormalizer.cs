using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

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
        var city = (settings.WeatherCity ?? string.Empty).Trim();

        var normalized = settings with
        {
            Language = Enum.IsDefined(settings.Language) ? settings.Language : d.Language,
            FocusMinutes = Math.Clamp(settings.FocusMinutes, SettingsLimits.MinFocusMinutes, SettingsLimits.MaxFocusMinutes),
            ShortBreakMinutes = Math.Clamp(settings.ShortBreakMinutes, SettingsLimits.MinBreakMinutes, SettingsLimits.MaxBreakMinutes),
            LongBreakMinutes = Math.Clamp(settings.LongBreakMinutes, SettingsLimits.MinBreakMinutes, SettingsLimits.MaxBreakMinutes),
            SchemaVersion = DockSettings.CurrentSchemaVersion,
            Edge = Enum.IsDefined(settings.Edge) ? settings.Edge : d.Edge,
            Size = Enum.IsDefined(settings.Size) ? settings.Size : d.Size,
            Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : d.Theme,
            LauncherHotkey = Enum.IsDefined(settings.LauncherHotkey) ? settings.LauncherHotkey : d.LauncherHotkey,
            MonitorDeviceName = string.IsNullOrWhiteSpace(settings.MonitorDeviceName) ? null : settings.MonitorDeviceName.Trim(),
            Spacing = Clamp(settings.Spacing, SettingsLimits.MinSpacing, SettingsLimits.MaxSpacing, d.Spacing),
            Opacity = Clamp(settings.Opacity, SettingsLimits.MinOpacity, SettingsLimits.MaxOpacity, d.Opacity),
            EdgeOffset = Clamp(settings.EdgeOffset, SettingsLimits.MinEdgeOffset, SettingsLimits.MaxEdgeOffset, d.EdgeOffset),
            WeatherCity = city.Length > SettingsLimits.MaxCityLength ? city[..SettingsLimits.MaxCityLength] : city,
            Shortcuts = NormalizeShortcuts(settings.Shortcuts),
            Widgets = NormalizeWidgets(settings.Widgets),
            DisplayLayouts = (settings.DisplayLayouts ?? [])
                .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.DeviceName))
                .GroupBy(l => l.DeviceName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Take(SettingsLimits.MaxProfiles)
                .Select(g => new DisplayLayout(g.Key, NormalizeWidgets(g.First().Widgets)))
                .ToList(),
        };

        return NormalizeProfiles(normalized);
    }

    public const string DefaultProfileId = "default";

    /// <summary>
    /// Guarantees at least one profile, valid active/default IDs, and that the active profile holds
    /// exactly the current <see cref="DockSettings.Widgets"/> (one source of truth for the bar).
    /// </summary>
    private static DockSettings NormalizeProfiles(DockSettings s)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var profiles = new List<DockProfile>();

        foreach (var p in s.Profiles ?? [])
        {
            if (p is null || profiles.Count >= SettingsLimits.MaxProfiles)
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id) ? NewUniqueId(ids) : p.Id;
            var name = string.IsNullOrWhiteSpace(p.Name) ? "Profile" : p.Name.Trim();
            if (name.Length > SettingsLimits.MaxProfileNameLength)
            {
                name = name[..SettingsLimits.MaxProfileNameLength];
            }

            profiles.Add(new DockProfile(id, name, NormalizeWidgets(p.Widgets), NormalizeTrigger(p.Trigger)));
        }

        if (profiles.Count == 0)
        {
            profiles.Add(new DockProfile(DefaultProfileId, "Everyday", s.Widgets, ProfileTrigger.None));
        }

        var active = profiles.Any(p => p.Id == s.ActiveProfileId) ? s.ActiveProfileId! : profiles[0].Id;
        var fallback = profiles.Any(p => p.Id == s.DefaultProfileId) ? s.DefaultProfileId! : active;

        var index = profiles.FindIndex(p => p.Id == active);
        profiles[index] = profiles[index] with { Widgets = s.Widgets };

        return s with { Profiles = profiles, ActiveProfileId = active, DefaultProfileId = fallback };
    }

    internal static ProfileTrigger NormalizeTrigger(ProfileTrigger? t)
    {
        if (t is null)
        {
            return ProfileTrigger.None;
        }

        var days = (t.Days ?? []).Where(Enum.IsDefined).Distinct().OrderBy(x => ((int)x + 6) % 7).ToList();
        var apps = (t.Apps ?? [])
            .Select(a => (a ?? string.Empty).Trim())
            .Select(a => a.Contains('\\') || a.Contains('/') ? Path.GetFileName(a) : a)
            .Where(a => a.Length > 0)
            .Select(a => a.ToLowerInvariant())
            .Distinct()
            .Take(SettingsLimits.MaxTriggerApps)
            .ToList();

        return new ProfileTrigger(
            t.ScheduleEnabled,
            Math.Clamp(t.FromMinutes, 0, 1439),
            Math.Clamp(t.ToMinutes, 0, 1439),
            days,
            apps);
    }

    internal static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static IReadOnlyList<WidgetPlacement> NormalizeWidgets(IReadOnlyList<WidgetPlacement>? widgets)
    {
        if (widgets is null || widgets.Count == 0)
        {
            return Array.Empty<WidgetPlacement>();
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var singletons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WidgetPlacement>(widgets.Count);

        foreach (var w in widgets)
        {
            var descriptor = WidgetCatalog.Get(w?.TypeId);
            if (w is null || descriptor is null)
            {
                continue; // unknown or removed widget type
            }

            if (!descriptor.AllowMultiple && !singletons.Add(descriptor.TypeId))
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(w.Id) || !ids.Add(w.Id) ? NewUniqueId(ids) : w.Id;
            result.Add(new WidgetPlacement(id, descriptor.TypeId));

            if (result.Count >= SettingsLimits.MaxWidgets)
            {
                break;
            }
        }

        return result;
    }

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
