using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;

namespace NexaDock.Core.Settings;

/// <summary>
/// Holds the live settings, notifies listeners and persists changes after a short debounce so slider
/// drags do not hammer the database.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly TimeSpan _debounce;
    private readonly object _saveLock = new();
    private readonly Timer _timer;
    private DockSettings? _pending;
    private bool _disposed;

    public SettingsService(ISettingsStore store)
        : this(store, TimeSpan.FromMilliseconds(400))
    {
    }

    public SettingsService(ISettingsStore store, TimeSpan debounce)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _debounce = debounce;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public DockSettings Current { get; private set; } = SettingsNormalizer.Normalize(DockSettings.Default);

    public SettingsLoadStatus LoadStatus { get; private set; } = SettingsLoadStatus.NotFound;

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public void Load()
    {
        var result = _store.Load();
        Current = SettingsNormalizer.Normalize(result.Settings);
        LoadStatus = result.Status;
    }

    public void Update(Func<DockSettings, DockSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var previous = Current;
        var next = SettingsNormalizer.Normalize(mutate(previous));
        if (next.Equals(previous))
        {
            return;
        }

        Current = next;
        lock (_saveLock)
        {
            _pending = next;
        }

        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, next));
    }

    public void Flush()
    {
        lock (_saveLock)
        {
            if (_pending is null)
            {
                return;
            }

            try
            {
                _store.Save(_pending);
                _pending = null;
                LastSaveError = null;
            }
            catch (Exception ex)
            {
                // Keep the pending value so a later change or Flush retries; never crash the timer thread.
                LastSaveError = ex;
                SaveFailed?.Invoke(this, ex);
            }
        }
    }

    /// <summary>The most recent persistence failure, or <c>null</c> after a successful save.</summary>
    public Exception? LastSaveError { get; private set; }

    /// <summary>Raised (on an arbitrary thread) when persisting fails.</summary>
    public event EventHandler<Exception>? SaveFailed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        Flush();
    }
}
