using NexaDock.Core;

namespace NexaDock.Infrastructure;

/// <summary>Per-user locations for local data. Nothing is written outside %LOCALAPPDATA%\NexaDock.</summary>
public static class AppPaths
{
    /// <summary>Allows tests or portable runs to redirect data (environment variable NEXADOCK_DATA_DIR).</summary>
    public const string DataDirOverrideVariable = "NEXADOCK_DATA_DIR";

    public static string DataDirectory
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(DataDirOverrideVariable);
            var dir = !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.DataFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, ProductInfo.DatabaseFileName);

    public static string LogDirectory
    {
        get
        {
            var dir = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LogPath => Path.Combine(LogDirectory, ProductInfo.LogFileName);
}
