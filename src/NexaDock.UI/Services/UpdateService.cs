using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Pulse;
using NexaDock.Core.Updates;
using NexaDock.UI.Localization;

namespace NexaDock.UI.Services;

/// <summary>
/// Looks for a newer release on GitHub shortly after start and twice a day, tells the user through
/// Pulse, and installs it on request: download, checksum, unpack, then a small script waits for
/// NexaDock to close, copies the new files over the install folder and starts it again.
/// Settings and data live elsewhere (%LOCALAPPDATA%\NexaDock) and are untouched.
/// </summary>
public sealed partial class UpdateService : ObservableObject
{
    private const string PulseId = "update";

    private readonly IUpdateSource _source;
    private readonly PulseCenter _pulse;
    private readonly IAppShell _shell;
    private readonly ILogger<UpdateService> _logger;
    private readonly DispatcherTimer _timer;
    private UpdateInfo? _available;

    public UpdateService(IUpdateSource source, PulseCenter pulse, IAppShell shell, ILogger<UpdateService> logger)
    {
        _source = source;
        _pulse = pulse;
        _shell = shell;
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(2) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(12);
            await CheckAsync(quiet: true);
        };
        StatusText = Loc.F("update.current", Core.ProductInfo.Version);
    }

    /// <summary>Updates can be installed only into the per-user install made by install.ps1.</summary>
    public static bool IsInstalledCopy
    {
        get
        {
            var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Core.ProductInfo.Name);
            return string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), Path.GetFullPath(programs).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    [ObservableProperty] private string _statusText;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public void Start() => _timer.Start();

    [RelayCommand]
    private Task Check() => CheckAsync(quiet: false);

    private async Task CheckAsync(bool quiet)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        if (!quiet)
        {
            StatusText = Loc.T("update.checking");
        }

        try
        {
            var latest = await Task.Run(() => _source.GetLatestAsync(CancellationToken.None));
            if (latest is not null && UpdateRules.IsNewer(latest.Version, UpdateRules.Current))
            {
                _available = latest;
                IsAvailable = true;
                StatusText = Loc.F("update.available", latest.Version.ToString(3));
                _pulse.Raise(new PulseAlert(PulseId, null, PulseSeverity.Info, "pulse.update", [latest.Version.ToString(3)], 0xE896));
                _logger.LogInformation("Update {Version} is available.", latest.Version);
            }
            else
            {
                _available = null;
                IsAvailable = false;
                StatusText = latest is null && !quiet
                    ? Loc.T("update.noReleases")
                    : Loc.F("update.current", Core.ProductInfo.Version);
                _pulse.Clear(PulseId);
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogInformation("Update check failed: {Error}", ex.Message);
            if (!quiet)
            {
                StatusText = Loc.T("update.offline");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Install()
    {
        if (_available is not { } update || IsBusy)
        {
            return;
        }

        if (!IsInstalledCopy)
        {
            StatusText = Loc.T("update.notInstalled");
            return;
        }

        IsBusy = true;
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Core.ProductInfo.DataFolderName, "updates");
            StatusText = Loc.F("update.downloading", update.Version.ToString(3));
            var progress = new Progress<double>(p => Progress = p * 100);
            var package = await Task.Run(() => _source.DownloadAsync(update, data, progress, CancellationToken.None));

            var staging = Path.Combine(data, "staging-" + update.Version);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(package, staging));
            var newExe = Path.Combine(staging, Core.ProductInfo.Name + ".exe");
            if (!File.Exists(newExe))
            {
                throw new InvalidDataException("The update package does not contain NexaDock.exe.");
            }

            var script = Path.Combine(data, "apply-update.ps1");
            await File.WriteAllTextAsync(script, ApplyScript);
            var installDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[]
                     {
                         "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                         "-ProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         "-Source", staging, "-Target", installDir, "-Package", package,
                     })
            {
                start.ArgumentList.Add(arg);
            }

            Process.Start(start);
            _logger.LogInformation("Installing update {Version}; NexaDock restarts.", update.Version);
            _shell.Exit(); // the script copies the files once this process is gone, then starts it
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update failed.");
            StatusText = Loc.F("update.failed", ex.Message);
            IsBusy = false;
        }
    }

    /// <summary>Waits for NexaDock to exit, copies the new files over it and starts it again.</summary>
    private const string ApplyScript = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Package)
        $ErrorActionPreference = 'Continue'
        Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        robocopy $Source $Target /E /R:5 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        Start-Process -FilePath (Join-Path $Target 'NexaDock.exe')
        Remove-Item -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $Package -Force -ErrorAction SilentlyContinue
        """;
}
