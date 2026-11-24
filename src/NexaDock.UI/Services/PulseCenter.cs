using System.Collections.ObjectModel;
using System.Windows.Threading;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Pulse;

namespace NexaDock.UI.Services;

/// <summary>
/// Collects things that need the user's attention. System conditions (battery, disk, CPU) are
/// checked here; widgets raise their own (finished timers). A dismissed alert stays quiet until its
/// condition clears and comes back.
/// </summary>
public sealed class PulseCenter
{
    private readonly ISettingsService _settings;
    private readonly ISystemMonitor _system;
    private readonly IStorageNetworkMonitor _storage;
    private readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);
    private readonly List<double> _cpuSamples = [];
    private readonly DispatcherTimer _fast;
    private readonly DispatcherTimer _slow;

    public PulseCenter(ISettingsService settings, ISystemMonitor system, IStorageNetworkMonitor storage)
    {
        _settings = settings;
        _system = system;
        _storage = storage;
        _fast = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _fast.Tick += (_, _) => { if (!QuietMode.IsActive) CheckCpu(); };
        _slow = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _slow.Tick += (_, _) => { if (!QuietMode.IsActive) CheckSlow(); };
        _settings.Changed += (_, e) =>
        {
            if (!e.Current.PulseEnabled && Alerts.Count > 0)
            {
                Alerts.Clear();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>Active alerts, most severe first.</summary>
    public ObservableCollection<PulseAlert> Alerts { get; } = [];

    public event EventHandler? Changed;

    public void Start()
    {
        _fast.Start();
        _slow.Start();
        CheckSlow();
    }

    public void Raise(PulseAlert alert)
    {
        if (!_settings.Current.PulseEnabled || _dismissed.Contains(alert.Id))
        {
            return;
        }

        var existing = Alerts.FirstOrDefault(a => a.Id == alert.Id);
        if (existing is not null && existing.Equals(alert))
        {
            return;
        }

        if (existing is not null)
        {
            Alerts.Remove(existing);
        }

        var index = 0;
        while (index < Alerts.Count && Alerts[index].Severity >= alert.Severity)
        {
            index++;
        }

        Alerts.Insert(index, alert);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The condition is gone (or was handled): remove and forget any dismissal.</summary>
    public void Clear(string id)
    {
        _dismissed.Remove(id);
        Remove(id);
    }

    /// <summary>The user does not want to hear about it again until it recurs.</summary>
    public void Dismiss(string id)
    {
        _dismissed.Add(id);
        Remove(id);
    }

    public void DismissAll()
    {
        foreach (var a in Alerts.ToList())
        {
            Dismiss(a.Id);
        }
    }

    /// <summary>Informational alerts for a widget are acknowledged when its card is opened.</summary>
    public void Acknowledge(string widgetTypeId)
    {
        foreach (var a in Alerts.Where(a => a.WidgetTypeId == widgetTypeId && a.Severity == PulseSeverity.Info).ToList())
        {
            Clear(a.Id);
        }
    }

    public bool IsActiveFor(string widgetTypeId) => Alerts.Any(a => a.WidgetTypeId == widgetTypeId);

    private void Remove(string id)
    {
        var existing = Alerts.FirstOrDefault(a => a.Id == id);
        if (existing is not null)
        {
            Alerts.Remove(existing);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CheckCpu()
    {
        _cpuSamples.Add(_system.Sample().CpuPercent);
        if (_cpuSamples.Count > PulseRules.CpuHotSamples * 2)
        {
            _cpuSamples.RemoveAt(0);
        }

        Apply("cpu", PulseRules.Cpu(_cpuSamples));
    }

    private void CheckSlow()
    {
        Apply("battery", PulseRules.Battery(_system.GetPower()));
        var drives = _storage.GetDrives().Select(d => new DriveSpace(d.Name, d.FreeBytes, d.TotalBytes, d.IsSystem));
        Apply("storage", PulseRules.Storage(drives));
    }

    /// <summary>Raises the alert while its rule fires; clears it (and its dismissal) once the rule stops.</summary>
    private void Apply(string id, PulseAlert? alert)
    {
        if (alert is null)
        {
            Clear(id);
        }
        else
        {
            Raise(alert);
        }
    }
}
