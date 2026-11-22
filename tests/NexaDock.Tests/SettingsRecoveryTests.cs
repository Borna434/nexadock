using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Settings;

namespace NexaDock.Tests;

public class SettingsNormalizerTests
{
    [Fact]
    public void Out_of_range_numbers_are_clamped()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings { Width = 10, Spacing = 99, Opacity = 5, EdgeOffset = -4 });

        Assert.Equal(SettingsLimits.MinWidth, s.Width);
        Assert.Equal(SettingsLimits.MaxSpacing, s.Spacing);
        Assert.Equal(SettingsLimits.MaxOpacity, s.Opacity);
        Assert.Equal(SettingsLimits.MinEdgeOffset, s.EdgeOffset);
    }

    [Fact]
    public void Non_finite_numbers_fall_back_to_defaults()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings { Width = double.NaN, Opacity = double.PositiveInfinity });

        Assert.Equal(DockSettings.Default.Width, s.Width);
        Assert.Equal(DockSettings.Default.Opacity, s.Opacity);
    }

    [Fact]
    public void Undefined_enum_values_are_reset()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings { Theme = (DockThemeId)42, Edge = (DockEdge)7, Size = (DockSizePreset)(-1) });

        Assert.Equal(DockSettings.Default.Theme, s.Theme);
        Assert.Equal(DockSettings.Default.Edge, s.Edge);
        Assert.Equal(DockSettings.Default.Size, s.Size);
    }

    [Fact]
    public void Shortcuts_are_cleaned_and_deduplicated()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings
        {
            Shortcuts =
            [
                new DockShortcut("a", "One", @"C:\one.exe"),
                new DockShortcut("a", "Two", @"C:\two.exe"),
                new DockShortcut("", "", @"C:\Tools\three.exe"),
                new DockShortcut("x", "Empty", "   "),
            ],
        });

        Assert.Equal(3, s.Shortcuts.Count);
        Assert.Equal(3, s.Shortcuts.Select(x => x.Id).Distinct().Count());
        Assert.Equal("a", s.Shortcuts[0].Id);
        Assert.Equal("three", s.Shortcuts[2].Name);
    }

    [Fact]
    public void Shortcut_count_is_capped()
    {
        var many = Enumerable.Range(0, 50).Select(i => new DockShortcut($"id{i}", $"App {i}", $@"C:\app{i}.exe")).ToList();
        var s = SettingsNormalizer.Normalize(new DockSettings { Shortcuts = many });

        Assert.Equal(SettingsLimits.MaxShortcuts, s.Shortcuts.Count);
    }

    [Fact]
    public void Null_settings_become_defaults() =>
        Assert.Equal(DockSettings.Default, SettingsNormalizer.Normalize(null));
}

public class SettingsSerializerTests
{
    [Fact]
    public void Round_trips_all_fields()
    {
        var original = new DockSettings
        {
            Edge = DockEdge.Top,
            MonitorDeviceName = @"\\.\DISPLAY2",
            Size = DockSizePreset.Large,
            Theme = DockThemeId.Aurora,
            Width = 1000,
            Spacing = 12,
            Opacity = 0.7,
            EdgeOffset = 20,
            ReducedMotion = true,
            ShortcutsInitialized = true,
            Shortcuts = [new DockShortcut("id1", "Notepad", @"C:\Windows\notepad.exe", "-x", @"C:\Windows")],
        };

        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"Edge\": ")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    public void Invalid_documents_return_null(string json) =>
        Assert.Null(SettingsSerializer.Deserialize(json));

    [Fact]
    public void Individual_bad_fields_fall_back_without_losing_the_rest()
    {
        const string json = """
            {
              "Theme": "Plasma",
              "Size": 99,
              "Edge": "top",
              "Width": "wide",
              "Opacity": 0.8,
              "ReducedMotion": "yes",
              "Shortcuts": [ { "Id": "k", "Name": "Keep", "Target": "C:\\keep.exe" }, 5, { "Name": "NoTarget" } ]
            }
            """;

        var s = SettingsSerializer.Deserialize(json)!;

        Assert.Equal(DockSettings.Default.Theme, s.Theme);
        Assert.Equal(DockSettings.Default.Size, s.Size);
        Assert.Equal(DockEdge.Top, s.Edge);
        Assert.Equal(DockSettings.Default.Width, s.Width);
        Assert.Equal(0.8, s.Opacity);
        Assert.False(s.ReducedMotion);
        Assert.Single(s.Shortcuts);
        Assert.Equal("Keep", s.Shortcuts[0].Name);
    }

    [Fact]
    public void Numeric_enum_strings_are_not_accepted_as_names()
    {
        var s = SettingsSerializer.Deserialize("""{ "Theme": "77" }""")!;
        Assert.Equal(DockSettings.Default.Theme, s.Theme);
    }
}

public class SettingsServiceTests
{
    private sealed class FakeStore : ISettingsStore
    {
        public DockSettings? Saved { get; private set; }
        public int SaveCount { get; private set; }
        public bool Fail { get; set; }
        public SettingsLoadResult Result { get; set; } = new(DockSettings.Default, SettingsLoadStatus.NotFound);

        public SettingsLoadResult Load() => Result;

        public void Save(DockSettings settings)
        {
            if (Fail)
            {
                throw new IOException("disk full");
            }

            Saved = settings;
            SaveCount++;
        }
    }

    [Fact]
    public void Update_normalizes_raises_changed_and_flush_persists()
    {
        var store = new FakeStore();
        using var service = new SettingsService(store, TimeSpan.FromHours(1));
        SettingsChangedEventArgs? args = null;
        service.Changed += (_, e) => args = e;

        service.Update(s => s with { Width = 5 });

        Assert.Equal(SettingsLimits.MinWidth, service.Current.Width);
        Assert.NotNull(args);
        Assert.Equal(DockSettings.Default.Width, args!.Previous.Width);
        Assert.Null(store.Saved); // debounced

        service.Flush();
        Assert.Equal(SettingsLimits.MinWidth, store.Saved!.Width);
    }

    [Fact]
    public void No_op_updates_do_not_notify_or_save()
    {
        var store = new FakeStore();
        using var service = new SettingsService(store, TimeSpan.FromHours(1));
        var raised = false;
        service.Changed += (_, _) => raised = true;

        service.Update(s => s with { });
        service.Flush();

        Assert.False(raised);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Debounced_save_happens_automatically()
    {
        var store = new FakeStore();
        using var service = new SettingsService(store, TimeSpan.FromMilliseconds(20));

        service.Update(s => s with { Theme = DockThemeId.Ember });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (store.Saved is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.Equal(DockThemeId.Ember, store.Saved?.Theme);
    }

    [Fact]
    public void Save_failures_are_reported_and_retried()
    {
        var store = new FakeStore { Fail = true };
        using var service = new SettingsService(store, TimeSpan.FromHours(1));
        Exception? reported = null;
        service.SaveFailed += (_, ex) => reported = ex;

        service.Update(s => s with { Spacing = 10 });
        service.Flush();
        Assert.IsType<IOException>(reported);
        Assert.NotNull(service.LastSaveError);

        store.Fail = false;
        service.Flush();
        Assert.Equal(10, store.Saved!.Spacing);
        Assert.Null(service.LastSaveError);
    }

    [Fact]
    public void Load_normalizes_stored_values()
    {
        var store = new FakeStore { Result = new(new DockSettings { Opacity = 3 }, SettingsLoadStatus.Loaded) };
        using var service = new SettingsService(store);

        service.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, service.LoadStatus);
        Assert.Equal(SettingsLimits.MaxOpacity, service.Current.Opacity);
    }
}
