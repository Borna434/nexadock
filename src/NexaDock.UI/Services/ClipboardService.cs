using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Clipboard;
using NexaDock.UI.Localization;

namespace NexaDock.UI.Services;

/// <summary>A clipboard entry as shown in the widget card and the Win+Alt+V window.</summary>
public sealed partial class ClipRow : ObservableObject
{
    public ClipRow(ClipEntry entry, ImageSource? thumbnail, string preview, string detail)
    {
        Entry = entry;
        Thumbnail = thumbnail;
        Preview = preview;
        Detail = detail;
    }

    public ClipEntry Entry { get; }

    public string Id => Entry.Id;

    public bool IsImage => Entry.Kind == ClipKind.Image;

    public bool IsPinned => Entry.Pinned;

    public ImageSource? Thumbnail { get; }

    /// <summary>First lines of the text, or "Image 1920 × 1080".</summary>
    public string Preview { get; }

    /// <summary>When it was copied ("2 min ago", "yesterday 14:05").</summary>
    public string Detail { get; }
}

/// <summary>
/// Clipboard history: records copies while it is switched on, keeps text across restarts (images
/// only for this session), and pastes an entry back into the app you were using.
/// </summary>
public sealed partial class ClipboardService : ObservableObject
{
    private const string StoreKey = "clipboard.history";

    private readonly IClipboardMonitor _monitor;
    private readonly IKeyValueStore _store;
    private readonly IWindowTracker _tracker;
    private readonly IShellCommands _shell;
    private readonly ILogger<ClipboardService> _logger;
    private readonly ClipboardHistory _history = new();
    private readonly DispatcherTimer _save;
    private readonly Dictionary<string, ImageSource> _thumbnails = [];
    private bool _running;

    public ClipboardService(IClipboardMonitor monitor, IKeyValueStore store, IWindowTracker tracker, IShellCommands shell, ILogger<ClipboardService> logger)
    {
        _monitor = monitor;
        _store = store;
        _tracker = tracker;
        _shell = shell;
        _logger = logger;
        _history.Load(_store.Get(StoreKey));
        _monitor.Captured += OnCaptured;
        _save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            _store.Set(StoreKey, _history.Serialize());
        };
        Rebuild();
    }

    /// <summary>Raised when entries change (the launcher refreshes its list).</summary>
    public event EventHandler? Changed;

    public ObservableCollection<ClipRow> Items { get; } = [];

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isEnabled;

    partial void OnQueryChanged(string value) => Rebuild();

    /// <summary>Starts or stops recording (the "Clipboard history" setting).</summary>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        if (enabled && !_running)
        {
            _monitor.Start();
            _running = true;
        }
        else if (!enabled && _running)
        {
            _monitor.Stop();
            _running = false;
        }
    }

    public IReadOnlyList<ClipRow> View(string? query) => _history.View(query).Select(ToRow).ToList();

    private void OnCaptured(object? sender, ClipCapture capture)
    {
        var now = DateTimeOffset.Now;
        var entry = capture.Text is not null
            ? ClipEntry.FromText(capture.Text, now)
            : ClipEntry.FromImage(capture.Png!, capture.Width, capture.Height, now);
        if (_history.Add(entry))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            ScheduleSave();
            Rebuild();
        }
    }

    /// <summary>Puts the entry on the clipboard and pastes it where you were working.</summary>
    public void Paste(string id, IntPtr target)
    {
        var entry = _history.Items.FirstOrDefault(i => i.Id == id);
        if (entry is null)
        {
            return;
        }

        try
        {
            if (entry.Kind == ClipKind.Text)
            {
                _monitor.SetText(entry.Text!);
            }
            else
            {
                _monitor.SetImage(entry.Png!);
            }

            // It becomes the newest entry again.
            _history.Add(entry with { Copied = DateTimeOffset.Now });
            ScheduleSave();
            Rebuild();
            _shell.PasteInto(target != IntPtr.Zero ? target : _tracker.ForegroundWindow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pasting from the clipboard history failed.");
        }
    }

    /// <summary>Pastes into the app that was in front before the bar was clicked.</summary>
    [RelayCommand]
    private void PasteRow(ClipRow? row)
    {
        if (row is not null)
        {
            Paste(row.Id, _tracker.ForegroundWindow);
            PastedFromCard?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The widget card closes after a paste.</summary>
    public event EventHandler? PastedFromCard;

    [RelayCommand]
    public void TogglePin(ClipRow? row)
    {
        if (row is null)
        {
            return;
        }

        _history.TogglePin(row.Id);
        ScheduleSave();
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Delete(ClipRow? row)
    {
        if (row is null)
        {
            return;
        }

        _history.Remove(row.Id);
        _thumbnails.Remove(row.Id);
        ScheduleSave();
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearAll()
    {
        _history.ClearUnpinned();
        ScheduleSave();
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleSave()
    {
        _save.Stop();
        _save.Start();
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var row in View(Query))
        {
            Items.Add(row);
        }

        Count = _history.Items.Count;
    }

    private ClipRow ToRow(ClipEntry e)
    {
        var detail = When(e.Copied);
        if (e.Kind == ClipKind.Image)
        {
            return new ClipRow(e, Thumbnail(e), Loc.F("clipboard.image", e.Width, e.Height), detail);
        }

        var text = e.Text!.Trim().Replace("\r", string.Empty, StringComparison.Ordinal);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var preview = string.Join(" ⏎ ", lines.Take(3).Select(l => l.Trim()));
        if (preview.Length > 160)
        {
            preview = preview[..160] + "…";
        }

        return new ClipRow(e, null, preview, detail);
    }

    private ImageSource? Thumbnail(ClipEntry e)
    {
        if (_thumbnails.TryGetValue(e.Id, out var cached))
        {
            return cached;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = 96;
            image.StreamSource = new MemoryStream(e.Png!);
            image.EndInit();
            image.Freeze();
            _thumbnails[e.Id] = image;
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string When(DateTimeOffset copied)
    {
        var culture = Loc.Instance.Culture;
        var age = DateTimeOffset.Now - copied;
        if (age < TimeSpan.FromMinutes(1))
        {
            return Loc.T("clipboard.justNow");
        }

        if (age < TimeSpan.FromHours(1))
        {
            return Loc.F("clipboard.minutesAgo", (int)age.TotalMinutes);
        }

        return copied.Date == DateTimeOffset.Now.Date
            ? copied.ToLocalTime().ToString("t", culture)
            : copied.ToLocalTime().ToString("d. MMM, t", culture);
    }
}
