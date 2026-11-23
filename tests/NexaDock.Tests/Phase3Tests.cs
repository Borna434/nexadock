using System.Text.RegularExpressions;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Localization;
using NexaDock.Core.Models;
using NexaDock.Core.Productivity;
using NexaDock.Core.Profiles;
using NexaDock.Core.Pulse;
using NexaDock.Core.Settings;
using NexaDock.Core.Widgets;
using NexaDock.Infrastructure.Storage;

namespace NexaDock.Tests;

internal sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

public class ProfileTests
{
    private static readonly DateTime Monday10 = new(2026, 11, 23, 10, 0, 0); // a Monday

    private static DockSettings WithProfiles(params DockProfile[] extra)
    {
        var baseline = SettingsNormalizer.Normalize(new DockSettings { Widgets = WidgetCatalog.DefaultLayout() });
        return SettingsNormalizer.Normalize(baseline with { Profiles = baseline.Profiles.Concat(extra).ToList() });
    }

    [Fact]
    public void Normalization_creates_a_default_profile_holding_the_current_layout()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings { Widgets = WidgetCatalog.DefaultLayout() });

        var profile = Assert.Single(s.Profiles);
        Assert.Equal(s.ActiveProfileId, profile.Id);
        Assert.Equal(s.Widgets, profile.Widgets);
        Assert.Equal(s.ActiveProfileId, s.DefaultProfileId);
    }

    [Fact]
    public void Active_profile_always_mirrors_the_bar_layout()
    {
        var s = WithProfiles();
        var edited = SettingsNormalizer.Normalize(s with { Widgets = [WidgetPlacement.Create(WidgetTypes.Clock)] });

        Assert.Single(edited.ActiveProfile!.Widgets);
    }

    [Fact]
    public void Activate_swaps_layouts_and_keeps_the_previous_one()
    {
        var work = ProfileTemplates.Create(ProfileTemplate.Work, "Work");
        var s = WithProfiles(work);
        var original = s.Widgets;

        var switched = SettingsNormalizer.Normalize(ProfileEngine.Activate(s, work.Id));
        Assert.Equal(work.Id, switched.ActiveProfileId);
        Assert.Equal(work.Widgets, switched.Widgets);

        var back = SettingsNormalizer.Normalize(ProfileEngine.Activate(switched, s.Profiles[0].Id));
        Assert.Equal(original, back.Widgets);
    }

    [Fact]
    public void App_trigger_beats_schedule_which_beats_default()
    {
        var work = ProfileTemplates.Create(ProfileTemplate.Work, "Work"); // weekdays 9–17
        var gaming = ProfileTemplates.Create(ProfileTemplate.Gaming, "Gaming"); // steam.exe
        var s = WithProfiles(work, gaming);

        Assert.Equal(gaming.Id, ProfileEngine.Resolve(s, Monday10, @"C:\Program Files (x86)\Steam\steam.exe"));
        Assert.Equal(work.Id, ProfileEngine.Resolve(s, Monday10, @"C:\Windows\notepad.exe"));
        Assert.Equal(s.DefaultProfileId, ProfileEngine.Resolve(s, Monday10.AddHours(10), null));
        Assert.Equal(s.DefaultProfileId, ProfileEngine.Resolve(s, Monday10.AddDays(5), null)); // Saturday
    }

    [Fact]
    public void Auto_switch_off_keeps_the_active_profile()
    {
        var gaming = ProfileTemplates.Create(ProfileTemplate.Gaming, "Gaming");
        var s = WithProfiles(gaming) with { AutoSwitchProfiles = false };

        Assert.Equal(s.ActiveProfileId, ProfileEngine.Resolve(s, Monday10, "steam.exe"));
    }

    [Theory]
    [InlineData(22 * 60, 6 * 60, 23, 0, true)]
    [InlineData(22 * 60, 6 * 60, 3, 0, true)]
    [InlineData(22 * 60, 6 * 60, 12, 0, false)]
    [InlineData(9 * 60, 17 * 60, 17, 0, false)]
    [InlineData(9 * 60, 17 * 60, 9, 0, true)]
    public void Schedules_handle_normal_and_overnight_windows(int from, int to, int hour, int minute, bool expected)
    {
        var trigger = new ProfileTrigger(true, from, to, [], []);
        Assert.Equal(expected, ProfileEngine.IsInSchedule(trigger, new DateTime(2026, 11, 24, hour, minute, 0)));
    }

    [Fact]
    public void Overnight_early_morning_counts_as_the_previous_day()
    {
        // Friday 22:00–06:00: Saturday 03:00 belongs to Friday's window.
        var trigger = new ProfileTrigger(true, 22 * 60, 6 * 60, [DayOfWeek.Friday], []);
        Assert.True(ProfileEngine.IsInSchedule(trigger, new DateTime(2026, 11, 28, 3, 0, 0)));
        Assert.False(ProfileEngine.IsInSchedule(trigger, new DateTime(2026, 11, 27, 3, 0, 0)));
    }

    [Fact]
    public void Triggers_are_cleaned()
    {
        var t = SettingsNormalizer.NormalizeTrigger(new ProfileTrigger(true, -5, 5000, [DayOfWeek.Monday, DayOfWeek.Monday],
            [@"C:\Games\Game.EXE", "  ", "steam.exe", "STEAM.EXE"]));

        Assert.Equal(0, t.FromMinutes);
        Assert.Equal(1439, t.ToMinutes);
        Assert.Single(t.Days);
        Assert.Equal(["game.exe", "steam.exe"], t.Apps);
    }

    [Fact]
    public void Profiles_round_trip_through_json()
    {
        var s = WithProfiles(ProfileTemplates.Create(ProfileTemplate.Study, "Učenje")) with
        {
            Language = AppLanguage.Croatian,
            PulseEnabled = false,
            FocusMinutes = 50,
            AutoSwitchProfiles = false,
        };

        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(s))!;
        Assert.Equal(SettingsNormalizer.Normalize(s), restored);
    }

    [Fact]
    public void Profile_count_and_focus_lengths_are_limited()
    {
        var many = Enumerable.Range(0, 20).Select(i => ProfileTemplates.Create(ProfileTemplate.Blank, $"P{i}")).ToArray();
        var s = SettingsNormalizer.Normalize(new DockSettings { Profiles = many, FocusMinutes = 1000, ShortBreakMinutes = 0 });

        Assert.Equal(SettingsLimits.MaxProfiles, s.Profiles.Count);
        Assert.Equal(SettingsLimits.MaxFocusMinutes, s.FocusMinutes);
        Assert.Equal(SettingsLimits.MinBreakMinutes, s.ShortBreakMinutes);
    }
}

public class PulseRuleTests
{
    [Theory]
    [InlineData(true, 50, false, null)]
    [InlineData(true, 15, false, PulseSeverity.Warning)]
    [InlineData(true, 5, false, PulseSeverity.Critical)]
    [InlineData(true, 5, true, null)]
    [InlineData(false, 5, false, null)]
    public void Battery(bool hasBattery, int percent, bool charging, PulseSeverity? expected) =>
        Assert.Equal(expected, PulseRules.Battery(new PowerStatus(hasBattery, percent, charging))?.Severity);

    [Fact]
    public void Storage_warns_on_low_fraction_or_low_absolute_space()
    {
        const ulong gb = 1024UL * 1024 * 1024;
        Assert.Null(PulseRules.Storage([new DriveSpace("C:", 200 * gb, 1000 * gb, true)]));
        Assert.NotNull(PulseRules.Storage([new DriveSpace("C:", 50 * gb, 1000 * gb, true)]));
        Assert.NotNull(PulseRules.Storage([new DriveSpace("C:", 8 * gb, 60 * gb, true)]));
        Assert.Equal(PulseSeverity.Critical, PulseRules.Storage([new DriveSpace("C:", 20 * gb, 1000 * gb, true)])!.Severity);
        Assert.Null(PulseRules.Storage([new DriveSpace("D:", 1 * gb, 1000 * gb, false)]));
    }

    [Fact]
    public void Cpu_needs_a_sustained_run_of_high_samples()
    {
        var high = Enumerable.Repeat(95.0, PulseRules.CpuHotSamples).ToList();
        Assert.NotNull(PulseRules.Cpu(high));

        var dip = high.ToList();
        dip[^3] = 40;
        Assert.Null(PulseRules.Cpu(dip));
        Assert.Null(PulseRules.Cpu(high.Take(3).ToList()));
    }
}

public class TimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Focus_session_cycles_focus_and_breaks_with_a_long_break_every_fourth_round()
    {
        var time = new FakeTime(T0);
        var s = new FocusSession(time) { FocusLength = TimeSpan.FromMinutes(25), ShortBreakLength = TimeSpan.FromMinutes(5), LongBreakLength = TimeSpan.FromMinutes(15) };

        s.Start();
        Assert.Equal(FocusPhase.Focus, s.Phase);

        var phases = new List<FocusPhase>();
        for (var i = 0; i < 8; i++)
        {
            time.Advance(s.Remaining);
            Assert.NotNull(s.Tick());
            phases.Add(s.Phase);
        }

        Assert.Equal(
            [FocusPhase.ShortBreak, FocusPhase.Focus, FocusPhase.ShortBreak, FocusPhase.Focus,
             FocusPhase.ShortBreak, FocusPhase.Focus, FocusPhase.LongBreak, FocusPhase.Focus], phases);
        Assert.Equal(4, s.CompletedRounds);
    }

    [Fact]
    public void Focus_pause_freezes_the_remaining_time()
    {
        var time = new FakeTime(T0);
        var s = new FocusSession(time);
        s.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        s.Pause();
        time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromMinutes(15), s.Remaining);
        Assert.Null(s.Tick());

        s.Start();
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(FocusPhase.Focus, s.Tick());
    }

    [Fact]
    public void Countdown_finishes_exactly_once()
    {
        var time = new FakeTime(T0);
        var c = new Countdown(time);
        c.Set(TimeSpan.FromMinutes(3));
        c.Start();
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(c.Tick());
        Assert.Equal(TimeSpan.FromMinutes(1), c.Remaining);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(c.Tick());
        Assert.False(c.Tick());
        Assert.True(c.IsFinished);
        Assert.Equal(TimeSpan.Zero, c.Remaining);
    }

    [Fact]
    public void Stopwatch_records_lap_splits_and_pauses()
    {
        var time = new FakeTime(T0);
        var w = new LapStopwatch(time);
        w.Start();
        time.Advance(TimeSpan.FromSeconds(10));
        w.Lap();
        time.Advance(TimeSpan.FromSeconds(4));
        w.Lap();
        w.Pause();
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromSeconds(14), w.Elapsed);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4)], w.Laps);

        w.Reset();
        Assert.Equal(TimeSpan.Zero, w.Elapsed);
        Assert.Empty(w.Laps);
    }
}

public sealed class KeyValueStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexadock-tests", Guid.NewGuid().ToString("N"));

    public KeyValueStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Values_persist_and_do_not_disturb_settings()
    {
        var path = Path.Combine(_dir, "kv.db");
        var store = new SqliteSettingsStore(path);
        store.Save(new DockSettings { Theme = DockThemeId.Aurora });
        store.Set("todos", "[1,2,3]");

        var reopened = new SqliteSettingsStore(path);
        Assert.Equal("[1,2,3]", reopened.Get("todos"));
        Assert.Equal(DockThemeId.Aurora, reopened.Load().Settings.Theme);

        reopened.Set("todos", null);
        Assert.Null(reopened.Get("todos"));
        Assert.Null(new SqliteSettingsStore(Path.Combine(_dir, "missing.db")).Get("x"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class LocalizationTests
{
    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var missingInCroatian = Strings.English.Keys.Except(Strings.Croatian.Keys).ToList();
        var extraInCroatian = Strings.Croatian.Keys.Except(Strings.English.Keys).ToList();

        Assert.True(missingInCroatian.Count == 0, "Missing Croatian: " + string.Join(", ", missingInCroatian));
        Assert.True(extraInCroatian.Count == 0, "Croatian-only: " + string.Join(", ", extraInCroatian));
    }

    [Fact]
    public void Format_placeholders_match_between_languages()
    {
        var placeholder = new Regex(@"\{(\d+)(:[^}]*)?\}");
        foreach (var (key, en) in Strings.English)
        {
            var hr = Strings.Croatian[key];
            var enArgs = placeholder.Matches(en).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var hrArgs = placeholder.Matches(hr).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            Assert.True(enArgs.SequenceEqual(hrArgs), $"Placeholders differ for {key}");
        }
    }

    [Fact]
    public void Every_widget_category_and_weather_kind_has_text()
    {
        var needed = WidgetCatalog.All.SelectMany(d => new[] { $"widget.{d.TypeId}.name", $"widget.{d.TypeId}.desc" })
            .Concat(Enum.GetNames<WidgetCategory>().Select(c => $"category.{c}"))
            .Concat(Enum.GetNames<WeatherKind>().Select(k => $"weather.code.{k}"))
            .Append("category.All")
            .Append("pulse.battery").Append("pulse.storage").Append("pulse.cpu");

        var missing = needed.Where(k => !Strings.English.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    }

    /// <summary>Scans the UI source for keys used in XAML and code, so no screen shows a raw key.</summary>
    [Fact]
    public void All_keys_used_in_the_source_exist()
    {
        var root = FindRepositoryRoot();
        var used = new HashSet<string>();
        var patterns = new[]
        {
            new Regex(@"\{l:Loc ([a-zA-Z0-9_.]+)\}"),
            new Regex(@"Loc\.[TF]\(""([a-zA-Z0-9_.]+)"""),
            new Regex(@"""((?:language|profiles\.template)\.[a-zA-Z0-9_.]+)"""),
        };

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (var p in patterns)
            {
                foreach (Match m in p.Matches(text))
                {
                    used.Add(m.Groups[1].Value);
                }
            }
        }

        Assert.NotEmpty(used);
        var missing = used.Where(k => !Strings.English.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Used but not defined: " + string.Join(", ", missing));
    }

    [Fact]
    public void Lookup_falls_back_to_english_then_key()
    {
        Assert.Equal("Zatvori", Strings.Get(AppLanguage.Croatian, "common.close"));
        Assert.Equal("Close", Strings.Get(AppLanguage.English, "common.close"));
        Assert.Equal("no.such.key", Strings.Get(AppLanguage.Croatian, "no.such.key"));
        Assert.Equal(AppLanguage.Croatian, Strings.Resolve(AppLanguage.System, new System.Globalization.CultureInfo("hr-HR")));
        Assert.Equal(AppLanguage.English, Strings.Resolve(AppLanguage.System, new System.Globalization.CultureInfo("de-DE")));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexaDock.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
