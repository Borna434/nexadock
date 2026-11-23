using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Launcher;
using NexaDock.Core.Models;
using NexaDock.UI.Localization;
using NexaDock.UI.Theming;

namespace NexaDock.UI.ViewModels;

/// <summary>One row in the launcher.</summary>
public sealed partial class LauncherRow : ObservableObject
{
    public LauncherRow(LauncherResult result, ImageSource? icon, string glyph, string subtitle)
    {
        Result = result;
        Icon = icon;
        Glyph = glyph;
        Subtitle = subtitle;
    }

    public LauncherResult Result { get; }

    public string Title => Result.Title;

    public string Subtitle { get; }

    public ImageSource? Icon { get; }

    /// <summary>Segoe Fluent glyph when there is no icon (calculator, web…).</summary>
    public string Glyph { get; }

    public bool HasIcon => Icon is not null;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Search-as-you-type over installed apps (instant), quick answers (calculator, unit conversion)
/// and indexed files (after a short pause), with a web search as the last line.
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    private const string UsageKey = "launcher.usage";
    private const int MaxFiles = 5;

    private readonly IAppIndex _apps;
    private readonly IFileSearch _files;
    private readonly IIconProvider _icons;
    private readonly IAppLauncher _launcher;
    private readonly IKeyValueStore _store;
    private readonly ILogger<LauncherViewModel> _logger;
    private readonly DispatcherTimer _fileDelay;
    private readonly Dictionary<string, int> _usage;
    private IReadOnlyList<AppEntry> _appList = [];
    private CancellationTokenSource? _fileCts;
    private int _iconSize = 32;

    public LauncherViewModel(IAppIndex apps, IFileSearch files, IIconProvider icons, IAppLauncher launcher, IKeyValueStore store, ILogger<LauncherViewModel> logger)
    {
        _apps = apps;
        _files = files;
        _icons = icons;
        _launcher = launcher;
        _store = store;
        _logger = logger;
        _usage = LoadUsage();
        _fileDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _fileDelay.Tick += async (_, _) =>
        {
            _fileDelay.Stop();
            await SearchFilesAsync();
        };
    }

    public ObservableCollection<LauncherRow> Rows { get; } = [];

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private string _hint = string.Empty;
    [ObservableProperty] private bool _hasRows;

    /// <summary>Raised when the launcher should close (after running something).</summary>
    public event EventHandler? Done;

    public void SetDpiScale(double scale) => _iconSize = Math.Clamp((int)Math.Round(24 * scale), 16, 64);

    /// <summary>Called when the window opens: start fresh and make sure the app list is loaded.</summary>
    public async void Open()
    {
        Query = string.Empty;
        Hint = Loc.T("launcher.hint");
        Rows.Clear();
        HasRows = false;
        try
        {
            _appList = await _apps.GetAppsAsync(CancellationToken.None);
            Update();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading apps for the launcher failed.");
        }
    }

    partial void OnQueryChanged(string value)
    {
        Update();
        _fileCts?.Cancel();
        _fileDelay.Stop();
        if (value.Trim().Length >= 2)
        {
            _fileDelay.Start();
        }
    }

    private void Update()
    {
        var results = LauncherEngine.Query(Query, _appList, _usage, Loc.Instance.Culture);
        var keepFiles = Rows.Where(r => r.Result.Kind is LauncherItemKind.File or LauncherItemKind.Folder).ToList();
        Rows.Clear();
        foreach (var r in results.Where(r => r.Kind != LauncherItemKind.Web))
        {
            Rows.Add(ToRow(r));
        }

        // Files from the previous keystroke stay until the new search answers, so the list doesn't jump.
        if (Query.Trim().Length >= 2)
        {
            foreach (var f in keepFiles.Where(f => FuzzyMatcher.Score(Query, f.Title) > 0))
            {
                Rows.Add(f);
            }
        }

        if (results.FirstOrDefault(r => r.Kind == LauncherItemKind.Web) is { } web)
        {
            Rows.Add(ToRow(web));
        }

        Select(0);
        HasRows = Rows.Count > 0;
    }

    private async Task SearchFilesAsync()
    {
        var text = Query.Trim();
        if (text.Length < 2)
        {
            return;
        }

        var cts = _fileCts = new CancellationTokenSource();
        IReadOnlyList<FileHit> hits;
        try
        {
            hits = await _files.SearchAsync(text, MaxFiles, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || text != Query.Trim())
        {
            return;
        }

        var selected = Rows.FirstOrDefault(r => r.IsSelected)?.Result;
        foreach (var old in Rows.Where(r => r.Result.Kind is LauncherItemKind.File or LauncherItemKind.Folder).ToList())
        {
            Rows.Remove(old);
        }

        var webIndex = Rows.ToList().FindIndex(r => r.Result.Kind == LauncherItemKind.Web);
        var insertAt = webIndex < 0 ? Rows.Count : webIndex;
        foreach (var hit in hits)
        {
            var kind = hit.IsFolder ? LauncherItemKind.Folder : LauncherItemKind.File;
            Rows.Insert(insertAt++, ToRow(new LauncherResult(hit.Name, hit.Path, kind, hit.Path, 0)));
        }

        Select(Math.Max(0, Rows.ToList().FindIndex(r => r.Result == selected)));
        HasRows = Rows.Count > 0;
    }

    private LauncherRow ToRow(LauncherResult r)
    {
        ImageSource? icon = null;
        if (r.Kind is LauncherItemKind.App or LauncherItemKind.File or LauncherItemKind.Folder && r.Target is not null)
        {
            icon = _icons.GetIcon(r.Target, _iconSize).ToBitmapSource();
        }

        var (glyph, subtitle) = r.Kind switch
        {
            LauncherItemKind.Calculation => (Core.Widgets.WidgetCatalog.Glyph(0xE8EF), Loc.F("launcher.copy", r.Subtitle)),
            LauncherItemKind.Conversion => (Core.Widgets.WidgetCatalog.Glyph(0xE8AB), Loc.F("launcher.copy", r.Subtitle)),
            LauncherItemKind.Web => (Core.Widgets.WidgetCatalog.Glyph(0xE774), Loc.T("launcher.web")),
            LauncherItemKind.Folder => (Core.Widgets.WidgetCatalog.Glyph(0xE8B7), ShortPath(r.Subtitle)),
            LauncherItemKind.File => (Core.Widgets.WidgetCatalog.Glyph(0xE8A5), ShortPath(r.Subtitle)),
            _ => (Core.Widgets.WidgetCatalog.Glyph(0xE71D), Loc.T("launcher.app")),
        };
        return new LauncherRow(r, icon, glyph, subtitle);
    }

    private static string ShortPath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? path;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return dir.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? "~" + dir[profile.Length..] : dir;
    }

    // ---- Keyboard ---------------------------------------------------------------------------------

    public void Move(int delta)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var index = Rows.ToList().FindIndex(r => r.IsSelected);
        Select(((index < 0 ? 0 : index) + delta + Rows.Count) % Rows.Count);
    }

    public void Select(int index)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].IsSelected = i == index;
        }
    }

    public LauncherRow? Selected => Rows.FirstOrDefault(r => r.IsSelected);

    /// <summary>Enter: run, open or copy the selected result. Ctrl+Enter opens a file's folder.</summary>
    public void Activate(LauncherRow? row, bool openFolder = false)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        var r = row.Result;
        try
        {
            switch (r.Kind)
            {
                case LauncherItemKind.Calculation:
                case LauncherItemKind.Conversion:
                    Clipboard.SetText(r.CopyText ?? r.Title);
                    break;

                case LauncherItemKind.App:
                    _launcher.Launch(DockShortcut.Create(r.Title, r.Target!));
                    Count(r.Target!);
                    break;

                case LauncherItemKind.File or LauncherItemKind.Folder when openFolder:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{r.Target}\"") { UseShellExecute = true });
                    break;

                default:
                    Process.Start(new ProcessStartInfo(r.Target!) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Launcher could not open {Kind}.", r.Kind);
        }

        Done?.Invoke(this, EventArgs.Empty);
    }

    private void Count(string target)
    {
        _usage[target] = _usage.GetValueOrDefault(target) + 1;
        if (_usage.Count > 300)
        {
            foreach (var rare in _usage.OrderBy(u => u.Value).Take(100).Select(u => u.Key).ToList())
            {
                _usage.Remove(rare);
            }
        }

        _store.Set(UsageKey, JsonSerializer.Serialize(_usage));
    }

    private Dictionary<string, int> LoadUsage()
    {
        try
        {
            return _store.Get(UsageKey) is { } json ? JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
