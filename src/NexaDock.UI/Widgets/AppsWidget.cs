using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.UI.Theming;

namespace NexaDock.UI.Widgets;

/// <summary>
/// Start, pinned apps and running windows, merged the way a taskbar does: a pinned app lights up
/// when it runs, other running apps appear after the pinned ones.
/// </summary>
public sealed partial class AppsWidget : WidgetViewModel
{
    private const string AppsFolder = @"shell:AppsFolder\";
    private readonly ISettingsService _settings;
    private readonly IWindowTracker _tracker;
    private readonly IAppLauncher _launcher;
    private readonly IIconProvider _icons;
    private readonly IShortcutCatalog _catalog;
    private readonly IShellCommands _shell;
    private readonly IAppShell _appShell;
    private readonly List<string> _runningOrder = [];
    private IReadOnlyList<DockShortcut> _pinned = [];
    private int _iconSize = 64;

    public AppsWidget(
        WidgetPlacement placement,
        ISettingsService settings,
        IWindowTracker tracker,
        IAppLauncher launcher,
        IIconProvider icons,
        IShortcutCatalog catalog,
        IShellCommands shell,
        IAppShell appShell)
        : base(placement)
    {
        _settings = settings;
        _tracker = tracker;
        _launcher = launcher;
        _icons = icons;
        _catalog = catalog;
        _shell = shell;
        _appShell = appShell;
    }

    public override bool HasPlate => false;

    public ObservableCollection<AppItemViewModel> Items { get; } = [];

    public override void ApplySettings(DockSettings settings)
    {
        if (!settings.Shortcuts.SequenceEqual(_pinned))
        {
            _pinned = settings.Shortcuts;
            Rebuild();
        }
    }

    public override void SetDpiScale(double scale)
    {
        var size = IconImageExtensions.IconRequestSize(34, scale);
        if (size != _iconSize)
        {
            _iconSize = size;
            foreach (var item in Items)
            {
                item.Icon = _icons.GetIcon(item.IconTarget, _iconSize).ToBitmapSource();
            }
        }
    }

    public override void Start()
    {
        _tracker.Changed += OnWindowsChanged;
        _tracker.Start();
        Rebuild();
    }

    private void OnWindowsChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var windows = _tracker.Windows;
        var used = new HashSet<IntPtr>();
        var groups = new List<AppGroup>();

        foreach (var pin in _pinned)
        {
            var identity = _catalog.Identify(pin);
            var mine = windows.Where(w => !used.Contains(w.Handle) && identity.Matches(w)).ToList();
            mine.ForEach(w => used.Add(w.Handle));
            groups.Add(new AppGroup("pin:" + pin.Id, pin.Name, pin.Target, pin, mine));
        }

        var unpinned = windows.Where(w => !used.Contains(w.Handle))
            .GroupBy(KeyOf)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var key in unpinned.Keys.Where(k => !_runningOrder.Contains(k)))
        {
            _runningOrder.Add(key);
        }

        _runningOrder.RemoveAll(k => !unpinned.ContainsKey(k));
        foreach (var key in _runningOrder)
        {
            var ws = unpinned[key];
            var first = ws[0];
            var iconTarget = first.AppUserModelId is { } aumid ? AppsFolder + aumid : first.ExecutablePath ?? string.Empty;
            var name = first.AppUserModelId is null && first.ExecutablePath is { } exe ? _catalog.GetDisplayName(exe) : first.Title;
            groups.Add(new AppGroup(key, name, iconTarget, null, ws));
        }

        Reconcile(groups);
    }

    private static string KeyOf(TrackedWindow w) =>
        w.AppUserModelId ?? w.ExecutablePath?.ToUpperInvariant() ?? $"hwnd:{w.Handle}";

    private void Reconcile(List<AppGroup> groups)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var existing = Items.FirstOrDefault(x => x.Key == g.Key);
            if (existing is null)
            {
                existing = new AppItemViewModel(g.Key, g.IconTarget, OnItemClick)
                {
                    Icon = _icons.GetIcon(g.IconTarget, _iconSize).ToBitmapSource(),
                };
                Items.Insert(Math.Min(i, Items.Count), existing);
            }
            else
            {
                var at = Items.IndexOf(existing);
                if (at != i)
                {
                    Items.Move(at, Math.Min(i, Items.Count - 1));
                }
            }

            existing.Update(g.Name, g.Shortcut, g.Windows);
        }

        while (Items.Count > groups.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }

    private void OnItemClick(AppItemViewModel item)
    {
        if (item.Windows.Count == 0)
        {
            Launch(item);
            return;
        }

        var active = item.Windows.FirstOrDefault(w => w.Handle == _tracker.ForegroundWindow);
        if (active is null)
        {
            _tracker.Activate(item.Windows[0].Handle);
        }
        else if (item.Windows.Count == 1)
        {
            _tracker.Minimize(active.Handle);
        }
        else
        {
            // Cycle through the app's windows, like clicking a grouped taskbar button repeatedly.
            var next = item.Windows[(item.Windows.ToList().IndexOf(active) + 1) % item.Windows.Count];
            _tracker.Activate(next.Handle);
        }
    }

    public void Launch(AppItemViewModel item)
    {
        var shortcut = item.Shortcut ?? LaunchableFor(item);
        if (shortcut is null)
        {
            return;
        }

        var result = _launcher.Launch(shortcut);
        if (!result.Success)
        {
            _appShell.NotifyError($"Could not open {item.Name}", result.Error ?? "The app could not be started.");
        }
    }

    private static DockShortcut? LaunchableFor(AppItemViewModel item)
    {
        var target = item.IconTarget;
        if (string.IsNullOrWhiteSpace(target) || (!target.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase) && !File.Exists(target)))
        {
            return null;
        }

        return new DockShortcut("transient", item.Name, target);
    }

    public void Activate(IntPtr hwnd) => _tracker.Activate(hwnd);

    public void CloseWindow(IntPtr hwnd) => _tracker.Close(hwnd);

    [RelayCommand]
    private void OpenStart() => _shell.OpenStartMenu();

    /// <summary>Right-click menu for one app.</summary>
    public IEnumerable<WidgetMenuItem> GetItemMenu(AppItemViewModel item)
    {
        var launchable = item.Shortcut is not null || LaunchableFor(item) is not null;
        yield return new WidgetMenuItem(item.Windows.Count == 0 ? $"Open {item.Name}" : "New window", () => Launch(item), launchable);

        if (item.Shortcut is { } pinned)
        {
            yield return new WidgetMenuItem("Unpin from bar", () => _settings.Update(s => s with
            {
                Shortcuts = s.Shortcuts.Where(x => x.Id != pinned.Id).ToList(),
                ShortcutsInitialized = true,
            }));
        }
        else if (LaunchableFor(item) is { } candidate)
        {
            yield return new WidgetMenuItem("Pin to bar", () => _settings.Update(s => s with
            {
                Shortcuts = s.Shortcuts.Append(DockShortcut.Create(candidate.Name, candidate.Target)).ToList(),
                ShortcutsInitialized = true,
            }));
        }

        if (item.Windows.Count > 0)
        {
            yield return new WidgetMenuItem(item.Windows.Count == 1 ? "Close window" : $"Close all {item.Windows.Count} windows", () =>
            {
                foreach (var w in item.Windows)
                {
                    _tracker.Close(w.Handle);
                }
            });
        }
    }

    public override void Dispose()
    {
        _tracker.Changed -= OnWindowsChanged;
        base.Dispose();
    }

    private sealed record AppGroup(string Key, string Name, string IconTarget, DockShortcut? Shortcut, IReadOnlyList<TrackedWindow> Windows);
}

public sealed partial class AppItemViewModel : ObservableObject
{
    private readonly Action<AppItemViewModel> _click;

    public AppItemViewModel(string key, string iconTarget, Action<AppItemViewModel> click)
    {
        Key = key;
        IconTarget = iconTarget;
        _click = click;
    }

    public string Key { get; }

    public string IconTarget { get; }

    public DockShortcut? Shortcut { get; private set; }

    public IReadOnlyList<TrackedWindow> Windows { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private ImageSource? _icon;

    public bool HasIcon => Icon is not null;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private int _windowCount;
    /// <summary>Only for apps that are not running; running apps show the live preview instead.</summary>
    [ObservableProperty] private string? _toolTip;

    public string Monogram => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    internal void Update(string name, DockShortcut? shortcut, IReadOnlyList<TrackedWindow> windows)
    {
        Name = name;
        Shortcut = shortcut;
        Windows = windows;
        IsPinned = shortcut is not null;
        IsRunning = windows.Count > 0;
        IsActive = windows.Any(w => w.IsForeground);
        WindowCount = windows.Count;
        ToolTip = windows.Count == 0 ? name : null;
        OnPropertyChanged(nameof(Monogram));
        OnPropertyChanged(nameof(Windows));
    }

    [RelayCommand]
    private void Click() => _click(this);
}
