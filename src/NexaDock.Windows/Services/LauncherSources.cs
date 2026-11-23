using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Launcher;

namespace NexaDock.Windows.Services;

/// <summary>
/// Every app in Start (classic and Store apps alike) from the shell's AppsFolder. Read on a
/// background STA thread and cached for five minutes; targets are "shell:AppsFolder\…", which the
/// app launcher and icon provider already understand.
/// </summary>
public sealed class ShellAppIndex : IAppIndex
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private readonly ILogger<ShellAppIndex> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AppEntry> _cache = [];
    private DateTime _loaded = DateTime.MinValue;

    public ShellAppIndex(ILogger<ShellAppIndex> logger) => _logger = logger;

    public async Task<IReadOnlyList<AppEntry>> GetAppsAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _loaded < CacheFor && _cache.Count > 0)
        {
            return _cache;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - _loaded < CacheFor && _cache.Count > 0)
            {
                return _cache;
            }

            var apps = await RunStaAsync(ReadAppsFolder).ConfigureAwait(false);
            if (apps.Count > 0)
            {
                _cache = apps;
                _loaded = DateTime.UtcNow;
            }

            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<AppEntry> ReadAppsFolder()
    {
        var result = new List<AppEntry>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return result;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic? folder = shell.NameSpace("shell:AppsFolder");
            if (folder is null)
            {
                return result;
            }

            foreach (dynamic item in folder.Items())
            {
                string name = item.Name;
                string path = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || IsNoise(name, path))
                {
                    continue;
                }

                result.Add(new AppEntry(name, @"shell:AppsFolder\" + path));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the installed apps failed.");
        }

        _logger.LogInformation("Launcher: {Count} apps indexed.", result.Count);
        return result;
    }

    /// <summary>Uninstallers, help files and web links clutter results.</summary>
    private static bool IsNoise(string name, string path) =>
        name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Deinstall", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".chm", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    private static Task<T> RunStaAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        { IsBackground = true, Name = "NexaDock.AppIndex" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>
/// File and folder names from the Windows Search index (the same index Explorer uses), limited to
/// the user's own folders. Read-only queries through the documented Search.CollatorDSO provider.
/// </summary>
public sealed class WindowsFileSearch : IFileSearch
{
    private readonly ILogger<WindowsFileSearch> _logger;
    private bool _unavailable;

    public WindowsFileSearch(ILogger<WindowsFileSearch> logger) => _logger = logger;

    public Task<IReadOnlyList<FileHit>> SearchAsync(string text, int max, CancellationToken cancellationToken)
    {
        var term = text.Trim();
        if (_unavailable || term.Length < 2 || !OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<FileHit>>([]);
        }

        return Task.Run<IReadOnlyList<FileHit>>(() => Query(term, max, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<FileHit> Query(string term, int max, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var results = new List<FileHit>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Escape for the SQL-like query language: quotes doubled, LIKE wildcards neutralised.
        var like = term.Replace("'", "''").Replace("%", "[%]").Replace("_", "[_]");
        var scope = new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri.Replace("'", "''");
        var sql = $"SELECT TOP {Math.Clamp(max * 3, 3, 60)} System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType " +
                  $"FROM SYSTEMINDEX WHERE SCOPE='{scope}' AND System.FileName LIKE '%{like}%' " +
                  "ORDER BY System.DateModified DESC";

        try
        {
            using var connection = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            connection.Open();
            using var command = new OleDbCommand(sql, connection);
            using var reader = command.ExecuteReader();
            while (reader is not null && reader.Read() && results.Count < max)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = reader.IsDBNull(0) ? null : reader.GetString(0);
                var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                var type = reader.IsDBNull(2) ? string.Empty : reader.GetValue(2)?.ToString() ?? string.Empty;
                if (path is null || name is null || IsHidden(path))
                {
                    continue;
                }

                results.Add(new FileHit(path, name, type.Equals("Directory", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path)));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Windows Search may be disabled; don't try again this session.
            _unavailable = true;
            _logger.LogWarning("Windows Search is not available: {Error}", ex.Message);
        }

        return results;
    }

    /// <summary>App data and dot-folders are noise in a launcher.</summary>
    private static bool IsHidden(string path) =>
        path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\.", StringComparison.Ordinal)
        || path.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase);
}
