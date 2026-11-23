using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Profiles;

namespace NexaDock.App;

/// <summary>
/// Switches profiles automatically. It reacts to <em>changes</em> of the resolved profile (a trigger
/// starting or ending), so a profile the user picks by hand stays until the next trigger edge.
/// A result must be stable for a few seconds before it applies, so a quick Alt+Tab past a
/// trigger app does not reshuffle the bar.
/// </summary>
public sealed class ProfileCoordinator : IDisposable
{
    private static readonly TimeSpan Stability = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settings;
    private readonly IWindowTracker _tracker;
    private readonly ILogger<ProfileCoordinator> _logger;
    private readonly DispatcherTimer _timer;
    private string? _lastResolved;
    private string? _pending;
    private DateTime _pendingSince;

    public ProfileCoordinator(ISettingsService settings, IWindowTracker tracker, ILogger<ProfileCoordinator> logger)
    {
        _settings = settings;
        _tracker = tracker;
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public void Start()
    {
        _lastResolved = Resolve();
        _timer.Start();
    }

    private string Resolve()
    {
        var foreground = _tracker.Windows.FirstOrDefault(w => w.IsForeground)?.ExecutablePath;
        return ProfileEngine.Resolve(_settings.Current, DateTime.Now, foreground);
    }

    private void Evaluate()
    {
        var s = _settings.Current;
        if (!s.AutoSwitchProfiles || s.Profiles.Count < 2)
        {
            _pending = null;
            return;
        }

        var resolved = Resolve();
        if (resolved == _lastResolved)
        {
            _pending = null;
            return;
        }

        if (resolved != _pending)
        {
            _pending = resolved;
            _pendingSince = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _pendingSince < Stability)
        {
            return;
        }

        _lastResolved = resolved;
        _pending = null;
        if (s.ActiveProfileId != resolved)
        {
            var name = s.Profiles.FirstOrDefault(p => p.Id == resolved)?.Name;
            _logger.LogInformation("Switching to profile {Profile}.", name);
            _settings.Update(x => ProfileEngine.Activate(x, resolved));
        }
    }

    public void Dispose() => _timer.Stop();
}
