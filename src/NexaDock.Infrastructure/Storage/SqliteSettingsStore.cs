using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Settings;

namespace NexaDock.Infrastructure.Storage;

/// <summary>
/// Stores settings as a JSON document in a small SQLite key/value table. Corrupt databases are moved
/// aside (never deleted) and replaced with defaults.
/// </summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    internal const string SettingsKey = "dock.settings";
    internal const int StorageSchemaVersion = 1;

    private readonly string _databasePath;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public SqliteSettingsStore(string databasePath, ILogger<SqliteSettingsStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public string DatabasePath => _databasePath;

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_databasePath))
            {
                return new SettingsLoadResult(DockSettings.Default, SettingsLoadStatus.NotFound);
            }

            string? json;
            try
            {
                using var connection = Open();
                EnsureSchema(connection);
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
                cmd.Parameters.AddWithValue("$key", SettingsKey);
                json = cmd.ExecuteScalar() as string;
            }
            catch (SqliteException ex)
            {
                var backup = QuarantineDatabase();
                _logger.LogWarning(ex, "Settings database was unreadable; moved to {Backup} and using defaults.", backup);
                return new SettingsLoadResult(DockSettings.Default, SettingsLoadStatus.Recovered, $"Database unreadable: {ex.Message}");
            }

            if (json is null)
            {
                return new SettingsLoadResult(DockSettings.Default, SettingsLoadStatus.NotFound);
            }

            var settings = SettingsSerializer.Deserialize(json);
            if (settings is null)
            {
                _logger.LogWarning("Stored settings JSON was invalid; using defaults.");
                return new SettingsLoadResult(DockSettings.Default, SettingsLoadStatus.Recovered, "Stored settings were not valid JSON.");
            }

            return new SettingsLoadResult(settings, SettingsLoadStatus.Loaded);
        }
    }

    public void Save(DockSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = SettingsSerializer.Serialize(settings);

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            try
            {
                WriteJson(json);
            }
            catch (SqliteException ex)
            {
                // A corrupt file blocks writes too: set it aside and retry once on a fresh database.
                var backup = QuarantineDatabase();
                _logger.LogWarning(ex, "Settings database rejected a write; moved to {Backup} and retrying.", backup);
                WriteJson(json);
            }
        }
    }

    private void WriteJson(string json)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO settings (key, value, updated_utc) VALUES ($key, $value, $updated)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$key", SettingsKey);
        cmd.Parameters.AddWithValue("$value", json);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, // release the file handle immediately so recovery can move the file
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            PRAGMA user_version = {StorageSchemaVersion};
            """;
        cmd.ExecuteNonQuery();
    }

    private string? QuarantineDatabase()
    {
        SqliteConnection.ClearAllPools();
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        var backup = $"{_databasePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            File.Move(_databasePath, backup);
            return backup;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not move corrupt settings database aside.");
            return null;
        }
    }
}
