using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;

namespace NexaDock.App;

/// <summary>
/// Applies the "Replace the Windows taskbar" setting. The taskbar's original state is stored in
/// settings before the first change, so it is restored on exit, when the option is turned off, and
/// on the next start after a crash.
/// </summary>
public sealed class TaskbarCoordinator : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ITaskbarController _taskbar;
    private readonly ILogger<TaskbarCoordinator> _logger;
    private readonly DispatcherTimer _keepHidden;
    private bool _active;

    public TaskbarCoordinator(ISettingsService settings, ITaskbarController taskbar, ILogger<TaskbarCoordinator> logger)
    {
        _settings = settings;
        _taskbar = taskbar;
        _logger = logger;

        // Explorer re-shows its taskbar after it restarts or on some shell events; re-assert.
        _keepHidden = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _keepHidden.Tick += (_, _) =>
        {
            if (_active)
            {
                _taskbar.Hide();
            }
        };
    }

    public void Start()
    {
        _settings.Changed += (_, e) =>
        {
            if (e.Previous.ReplaceTaskbar != e.Current.ReplaceTaskbar)
            {
                Apply();
            }
        };
        Apply();
    }

    private void Apply()
    {
        var s = _settings.Current;
        try
        {
            if (s.ReplaceTaskbar)
            {
                if (s.TaskbarStateBeforeReplace is null)
                {
                    var state = _taskbar.GetState();
                    _settings.Update(x => x with { TaskbarStateBeforeReplace = state });
                    _settings.Flush(); // persist before touching the taskbar, for crash recovery
                }

                _taskbar.Hide();
                _active = true;
                _keepHidden.Start();
                _logger.LogInformation("Windows taskbar hidden (NexaDock replaces it).");
            }
            else
            {
                RestoreIfNeeded();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing the Windows taskbar failed.");
        }
    }

    /// <summary>Puts the taskbar back if NexaDock changed it (also after a previous crash).</summary>
    public void RestoreIfNeeded()
    {
        _keepHidden.Stop();
        _active = false;
        if (_settings.Current.TaskbarStateBeforeReplace is { } previous)
        {
            _taskbar.Restore(previous);
            _settings.Update(x => x with { TaskbarStateBeforeReplace = null });
            _settings.Flush();
        }
    }

    public void Dispose() => RestoreIfNeeded();
}
