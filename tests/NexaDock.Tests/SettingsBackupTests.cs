using NexaDock.Core.Models;
using NexaDock.Core.Settings;
using NexaDock.Core.Widgets;

namespace NexaDock.Tests;

public class SettingsBackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Layout_and_widget_data_survive_a_round_trip_without_sign_ins()
    {
        var settings = new DockSettings
        {
            Theme = DockThemeId.Aurora,
            WeatherCity = "Rijeka",
            Widgets = [WidgetPlacement.Create(WidgetTypes.Weather), WidgetPlacement.Create(WidgetTypes.Apps)],
        };
        var data = new Dictionary<string, string>
        {
            ["todos"] = "[{\"Text\":\"Kolokvij\"}]",
            ["calendar.feeds"] = "[\"https://example.com/a.ics\"]",
            ["google.refreshToken"] = "secret",
            ["google.clientSecret"] = "secret",
        };

        var json = SettingsBackup.Create(settings, data, Now);
        var back = SettingsBackup.Parse(json, new DockSettings());

        Assert.DoesNotContain("secret", json);
        Assert.Equal(DockThemeId.Aurora, back.Settings.Theme);
        Assert.Equal("Rijeka", back.Settings.WeatherCity);
        Assert.Equal(settings.Widgets.Select(w => w.TypeId), back.Settings.Widgets.Select(w => w.TypeId));
        Assert.Equal(2, back.Data.Count);
        Assert.Equal(data["todos"], back.Data["todos"]);
        Assert.Equal(Now, back.Created);
    }

    [Fact]
    public void Machine_specific_values_are_kept_from_this_pc()
    {
        var json = SettingsBackup.Create(new DockSettings { LaunchAtStartup = true, TaskbarStateBeforeReplace = 1 }, new Dictionary<string, string>(), Now);

        var back = SettingsBackup.Parse(json, new DockSettings { LaunchAtStartup = false, TaskbarStateBeforeReplace = 0 });

        Assert.False(back.Settings.LaunchAtStartup);
        Assert.Equal(0, back.Settings.TaskbarStateBeforeReplace);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"format\":\"something-else\",\"version\":1}")]
    [InlineData("{\"format\":\"nexadock-backup\",\"version\":99,\"settings\":{}}")]
    [InlineData("{\"format\":\"nexadock-backup\",\"version\":1}")]
    public void Anything_else_is_rejected(string json)
    {
        Assert.Throws<FormatException>(() => SettingsBackup.Parse(json, new DockSettings()));
    }
}
