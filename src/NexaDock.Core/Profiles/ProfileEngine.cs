using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

namespace NexaDock.Core.Profiles;

/// <summary>
/// Decides which profile should be active. Priority: an app trigger matching the app in front,
/// then a schedule that covers the current time, then the user's default profile.
/// </summary>
public static class ProfileEngine
{
    public static string Resolve(DockSettings settings, DateTime localNow, string? foregroundExecutable)
    {
        var profiles = settings.Profiles;
        var fallback = settings.DefaultProfileId ?? settings.ActiveProfileId ?? profiles.FirstOrDefault()?.Id ?? string.Empty;
        if (!settings.AutoSwitchProfiles || profiles.Count == 0)
        {
            return settings.ActiveProfileId ?? fallback;
        }

        var exe = ExecutableName(foregroundExecutable);
        if (exe is not null)
        {
            var byApp = profiles.FirstOrDefault(p => p.Trigger.Apps.Any(a => MatchesApp(a, exe)));
            if (byApp is not null)
            {
                return byApp.Id;
            }
        }

        var bySchedule = profiles.FirstOrDefault(p => IsInSchedule(p.Trigger, localNow));
        return bySchedule?.Id ?? fallback;
    }

    public static bool IsInSchedule(ProfileTrigger trigger, DateTime localNow)
    {
        if (!trigger.ScheduleEnabled || trigger.FromMinutes == trigger.ToMinutes)
        {
            return false;
        }

        var minute = localNow.Hour * 60 + localNow.Minute;
        var overnight = trigger.FromMinutes > trigger.ToMinutes;

        // For an overnight window (22:00–06:00) the early-morning part belongs to the previous day.
        var day = overnight && minute < trigger.ToMinutes ? localNow.AddDays(-1).DayOfWeek : localNow.DayOfWeek;
        if (trigger.Days.Count > 0 && !trigger.Days.Contains(day))
        {
            return false;
        }

        return overnight
            ? minute >= trigger.FromMinutes || minute < trigger.ToMinutes
            : minute >= trigger.FromMinutes && minute < trigger.ToMinutes;
    }

    private static string? ExecutableName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFileName(path).ToLowerInvariant();
    }

    private static bool MatchesApp(string trigger, string exe) =>
        string.Equals(trigger, exe, StringComparison.OrdinalIgnoreCase)
        || string.Equals(trigger + ".exe", exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>Switches to <paramref name="profileId"/>: its widgets become the bar layout.</summary>
    public static DockSettings Activate(DockSettings settings, string profileId)
    {
        var target = settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (target is null || settings.ActiveProfileId == profileId)
        {
            return settings;
        }

        // The current layout is already mirrored into the active profile by normalization.
        return settings with { ActiveProfileId = target.Id, Widgets = target.Widgets };
    }
}

public enum ProfileTemplate
{
    Work,
    Gaming,
    Study,
    Blank,
}

/// <summary>Ready-made profiles; names are supplied by the caller (localized).</summary>
public static class ProfileTemplates
{
    public static DockProfile Create(ProfileTemplate template, string name)
    {
        var id = Guid.NewGuid().ToString("N");
        IReadOnlyList<DayOfWeek> weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

        return template switch
        {
            ProfileTemplate.Work => new DockProfile(id, name,
                Layout(WidgetTypes.Apps, WidgetTypes.Spacer, WidgetTypes.Pulse, WidgetTypes.Focus, WidgetTypes.Todos,
                       WidgetTypes.System, WidgetTypes.Volume, WidgetTypes.Calendar, WidgetTypes.Clock),
                new ProfileTrigger(true, 9 * 60, 17 * 60, weekdays, [])),

            ProfileTemplate.Gaming => new DockProfile(id, name,
                Layout(WidgetTypes.Apps, WidgetTypes.Spacer, WidgetTypes.Pulse, WidgetTypes.NowPlaying, WidgetTypes.System,
                       WidgetTypes.Network, WidgetTypes.Volume, WidgetTypes.Microphone, WidgetTypes.Clock),
                new ProfileTrigger(false, 18 * 60, 23 * 60, [], ["steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe", "rockstarlauncher.exe"])),

            ProfileTemplate.Study => new DockProfile(id, name,
                Layout(WidgetTypes.Apps, WidgetTypes.Spacer, WidgetTypes.Pulse, WidgetTypes.Focus, WidgetTypes.Notes,
                       WidgetTypes.Todos, WidgetTypes.Weather, WidgetTypes.DayProgress, WidgetTypes.Clock),
                new ProfileTrigger(false, 8 * 60, 14 * 60, weekdays, [])),

            _ => new DockProfile(id, name, Layout(WidgetTypes.Apps, WidgetTypes.Spacer, WidgetTypes.Clock), ProfileTrigger.None),
        };
    }

    private static IReadOnlyList<WidgetPlacement> Layout(params string[] types) =>
        WidgetArranger.Arrange(types.Select(WidgetPlacement.Create).ToList());
}
