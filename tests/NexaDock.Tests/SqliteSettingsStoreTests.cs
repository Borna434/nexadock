using Microsoft.Data.Sqlite;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Infrastructure.Storage;

namespace NexaDock.Tests;

public sealed class SqliteSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexadock-tests", Guid.NewGuid().ToString("N"));

    public SqliteSettingsStoreTests() => Directory.CreateDirectory(_dir);

    private string DbPath => Path.Combine(_dir, "settings.db");

    [Fact]
    public void Missing_database_returns_defaults_as_not_found()
    {
        var result = new SqliteSettingsStore(DbPath).Load();

        Assert.Equal(SettingsLoadStatus.NotFound, result.Status);
        Assert.Equal(DockSettings.Default, result.Settings);
        Assert.False(File.Exists(DbPath)); // loading alone must not create files
    }

    [Fact]
    public void Settings_survive_a_restart()
    {
        var saved = new DockSettings
        {
            Theme = DockThemeId.Sage,
            Edge = DockEdge.Top,
            Width = 1111,
            ShortcutsInitialized = true,
            Shortcuts = [new DockShortcut("n", "Notepad", @"C:\Windows\notepad.exe")],
        };

        new SqliteSettingsStore(DbPath).Save(saved);

        // A brand-new store instance simulates the next app launch.
        var loaded = new SqliteSettingsStore(DbPath).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(saved, loaded.Settings);
    }

    [Fact]
    public void Saving_twice_overwrites_the_single_settings_row()
    {
        var store = new SqliteSettingsStore(DbPath);
        store.Save(new DockSettings { Spacing = 2 });
        store.Save(new DockSettings { Spacing = 14 });

        Assert.Equal(14, store.Load().Settings.Spacing);

        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM settings;";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Corrupt_database_file_is_quarantined_and_defaults_returned()
    {
        File.WriteAllText(DbPath, "this is definitely not an sqlite database, just garbage bytes......");

        var store = new SqliteSettingsStore(DbPath);
        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.Recovered, result.Status);
        Assert.Equal(DockSettings.Default, result.Settings);
        Assert.Single(Directory.GetFiles(_dir, "settings.db.corrupt-*"));

        // The store is usable again afterwards.
        store.Save(new DockSettings { Theme = DockThemeId.Ember });
        Assert.Equal(DockThemeId.Ember, store.Load().Settings.Theme);
    }

    [Fact]
    public void Invalid_json_row_is_reported_as_recovered()
    {
        var store = new SqliteSettingsStore(DbPath);
        store.Save(DockSettings.Default);

        using (var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE settings SET value = '{broken';";
            cmd.ExecuteNonQuery();
        }

        var result = store.Load();
        Assert.Equal(SettingsLoadStatus.Recovered, result.Status);
        Assert.Equal(DockSettings.Default, result.Settings);
    }

    [Fact]
    public void Partially_invalid_stored_values_are_repaired_on_load()
    {
        var store = new SqliteSettingsStore(DbPath);
        store.Save(DockSettings.Default);

        using (var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """UPDATE settings SET value = '{"Theme":"Aurora","Opacity":12,"Size":"Gigantic"}';""";
            cmd.ExecuteNonQuery();
        }

        var s = store.Load().Settings;
        Assert.Equal(DockThemeId.Aurora, s.Theme);
        Assert.Equal(SettingsLimits.MaxOpacity, s.Opacity);
        Assert.Equal(DockSettings.Default.Size, s.Size);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
