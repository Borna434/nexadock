using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.UI.ViewModels;
using NexaDock.UI.Views;

namespace NexaDock.App;

/// <summary>
/// Keeps one extra bar on every display other than the main bar's, while "Show on all displays"
/// is on. Bars appear and disappear as displays are plugged in or removed, and follow the main
/// bar when it is hidden or shown.
/// </summary>
public sealed class BarManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IMonitorProvider _monitors;
    private readonly IDockWindowHost _host;
    private readonly ILogger<BarManager> _logger;
    private readonly Dictionary<string, DockWindow> _bars = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;
    private bool _hidden;

    public BarManager(IServiceProvider services, ISettingsService settings, IMonitorProvider monitors, IDockWindowHost host, ILogger<BarManager> logger)
    {
        _services = services;
        _settings = settings;
        _monitors = monitors;
        _host = host;
        _logger = logger;
    }

    public IReadOnlyCollection<DockWindow> ExtraBars => _bars.Values;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _settings.Changed += OnSettingsChanged;
        _host.PlacementInvalidated += OnPlacementInvalidated;
        Sync();
    }

    public void ShowAll()
    {
        _hidden = false;
        foreach (var bar in _bars.Values)
        {
            bar.UserHidden = false;
            if (!bar.IsVisible)
            {
                bar.Show();
            }

            bar.Reposition();
        }
    }

    public void HideAll()
    {
        _hidden = true;
        foreach (var bar in _bars.Values)
        {
            bar.UserHidden = true;
            bar.Hide();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Sync();

    private void OnPlacementInvalidated(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        var s = _settings.Current;
        var monitors = _monitors.GetMonitors();
        var main = MonitorSelector.Select(monitors, s.MonitorDeviceName);
        var wanted = s.ShowOnAllDisplays && main is not null
            ? monitors.Where(m => !string.Equals(m.DeviceName, main.DeviceName, StringComparison.OrdinalIgnoreCase))
                      .Select(m => m.DeviceName)
                      .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in _bars.Keys.Where(d => !wanted.Contains(d)).ToList())
        {
            _logger.LogInformation("Removing the bar on {Display}.", device);
            var bar = _bars[device];
            _bars.Remove(device);
            bar.Close();
        }

        foreach (var device in wanted.Where(d => !_bars.ContainsKey(d)))
        {
            try
            {
                var target = new BarTarget(device);
                var viewModel = ActivatorUtilities.CreateInstance<DockViewModel>(_services, target);
                var bar = ActivatorUtilities.CreateInstance<DockWindow>(_services, viewModel, target);
                _bars[device] = bar;
                bar.UserHidden = _hidden;
                if (!_hidden)
                {
                    bar.Show();
                }

                _logger.LogInformation("Added a bar on {Display}.", device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not create the bar on {Display}.", device);
            }
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _host.PlacementInvalidated -= OnPlacementInvalidated;
        foreach (var bar in _bars.Values.ToList())
        {
            bar.Close(); // removes its app bar reservation
        }

        _bars.Clear();
    }
}
