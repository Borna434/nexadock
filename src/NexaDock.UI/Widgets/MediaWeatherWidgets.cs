using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;
using NexaDock.UI.Theming;

using NexaDock.UI.Localization;

using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

public sealed partial class WeatherWidget : WidgetViewModel
{
    private readonly IWeatherService _weather;
    private readonly IAppShell _shell;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;
    private string _city = string.Empty;

    public WeatherWidget(WidgetPlacement placement, IWeatherService weather, IAppShell shell)
        : base(placement)
    {
        _weather = weather;
        _shell = shell;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(15) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public override bool HasCard => HasData;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _needsCity = true;
    [ObservableProperty] private string _temperature = "–";
    [ObservableProperty] private WeatherKind _kind = WeatherKind.Cloud;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private string _condition = string.Empty;
    [ObservableProperty] private string _range = string.Empty;
    [ObservableProperty] private string _details = string.Empty;
    [ObservableProperty] private string _status = Loc.T("weather.setCity");

    public ObservableCollection<ForecastDay> Forecast { get; } = [];

    public override void ApplySettings(DockSettings settings)
    {
        if (!string.Equals(_city, settings.WeatherCity, StringComparison.Ordinal))
        {
            _city = settings.WeatherCity;
            _ = RefreshAsync();
        }
    }

    public override void OnLanguageChanged() => _ = RefreshAsync();

    public override void Start() => _timer.Start();

    public override void OnClick() => _shell.ShowSettings(); // only reached when no city/data yet

    private async Task RefreshAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        NeedsCity = string.IsNullOrWhiteSpace(_city);
        if (NeedsCity)
        {
            HasData = false;
            Status = Loc.T("weather.setCity");
            OnPropertyChanged(nameof(HasCard));
            return;
        }

        if (!HasData)
        {
            Status = Loc.T("common.loading");
        }

        try
        {
            var r = await _weather.GetAsync(_city, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            Temperature = $"{Math.Round(r.Temperature):0}°";
            Kind = WeatherCodes.Kind(r.Code, r.IsDay);
            Location = r.Location;
            Condition = Loc.T($"weather.code.{WeatherCodes.Kind(r.Code, r.IsDay)}");
            var today = r.Days.FirstOrDefault();
            Range = today is null ? string.Empty : Loc.F("weather.range", Math.Round(today.Max), Math.Round(today.Min));
            Details = Loc.F("weather.details", Math.Round(r.ApparentTemperature), r.Humidity, Math.Round(r.WindSpeed), RegionInfo.CurrentRegion.IsMetric ? "km/h" : "mph");
            Forecast.Clear();
            foreach (var d in r.Days.Skip(1).Take(4))
            {
                Forecast.Add(new ForecastDay(
                    d.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd", CultureInfo.CurrentCulture),
                    WeatherCodes.Kind(d.Code),
                    $"{Math.Round(d.Max):0}°",
                    $"{Math.Round(d.Min):0}°"));
            }

            HasData = true;
            Status = string.Empty;
        }
        catch (WeatherException ex)
        {
            if (!HasData)
            {
                Status = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? Loc.T("weather.notFound") : Loc.T("weather.offline");
            }
        }
        catch (OperationCanceledException)
        {
        }

        OnPropertyChanged(nameof(HasCard));
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    public override void Dispose()
    {
        _timer.Stop();
        _cts?.Cancel();
        base.Dispose();
    }
}

public sealed record ForecastDay(string Day, WeatherKind Kind, string High, string Low);

public sealed partial class NowPlayingWidget : WidgetViewModel
{
    private readonly IMediaService _media;
    private readonly DispatcherTimer _progress;
    private MediaInfo? _info;
    private DateTime _positionStamp;

    private readonly IAudioService _audio;
    private bool _settingVolume;

    public NowPlayingWidget(WidgetPlacement placement, IMediaService media, IAudioService audio)
        : base(placement)
    {
        _media = media;
        _audio = audio;
        _progress = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _progress.Tick += (_, _) => { if (!QuietMode.IsActive) UpdateProgress(); };
    }

    public override bool HasCard => HasMedia;

    public string PlayGlyph { get; } = WidgetCatalog.Glyph(0xE768);
    public string PauseGlyph { get; } = WidgetCatalog.Glyph(0xE769);
    public string PreviousGlyph { get; } = WidgetCatalog.Glyph(0xE892);
    public string NextGlyph { get; } = WidgetCatalog.Glyph(0xE893);
    public string NoteGlyph { get; } = WidgetCatalog.Glyph(0xE8D6);

    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _title = Loc.T("media.nothing");
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private ImageSource? _art;
    [ObservableProperty] private bool _hasArt;
    [ObservableProperty] private bool _canPrevious;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _positionText = string.Empty;
    [ObservableProperty] private string _durationText = string.Empty;
    [ObservableProperty] private bool _hasTimeline;
    [ObservableProperty] private bool _canSeek;

    /// <summary>The playing app's own volume (0–100), when it has an audio session.</summary>
    [ObservableProperty] private double _appVolume;
    [ObservableProperty] private bool _hasAppVolume;

    /// <summary>Card background tinted with the album art's colour.</summary>
    [ObservableProperty] private Brush? _artTint;

    [ObservableProperty] private Brush? _artAccent;

    partial void OnAppVolumeChanged(double value)
    {
        if (!_settingVolume && _info is not null)
        {
            _audio.SetAppVolume(_info.Source, value / 100);
        }
    }

    [RelayCommand]
    private async Task Seek(double percent)
    {
        if (_info is null || !HasTimeline)
        {
            return;
        }

        var target = TimeSpan.FromSeconds(_info.Duration.TotalSeconds * Math.Clamp(percent, 0, 100) / 100);
        await _media.SeekAsync(target);
    }

    public override void OnCardOpened() => ReadAppVolume();

    private void ReadAppVolume()
    {
        var level = _info is null ? null : _audio.GetAppVolume(_info.Source);
        HasAppVolume = level is not null;
        if (level is { } v)
        {
            _settingVolume = true;
            AppVolume = Math.Round(v * 100);
            _settingVolume = false;
        }
    }

    public string PlayPauseGlyph => IsPlaying ? PauseGlyph : PlayGlyph;

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseGlyph));

    public override async void Start()
    {
        _media.Changed += OnMediaChanged;
        await _media.StartAsync();
        Apply(_media.Current);
        _progress.Start();
    }

    private void OnMediaChanged(object? sender, EventArgs e) => Apply(_media.Current);

    public override void OnLanguageChanged() => Apply(_media.Current);

    private void Apply(MediaInfo? info)
    {
        var artChanged = !ReferenceEquals(_info?.Thumbnail, info?.Thumbnail);
        _info = info;
        _positionStamp = DateTime.UtcNow;
        HasMedia = info is not null;
        OnPropertyChanged(nameof(HasCard));

        if (info is null)
        {
            Title = Loc.T("media.nothing");
            Artist = string.Empty;
            Source = string.Empty;
            IsPlaying = false;
            Art = null;
            HasArt = false;
            HasTimeline = false;
            return;
        }

        Title = info.Title;
        Artist = info.Artist;
        Source = FriendlySource(info.Source);
        IsPlaying = info.IsPlaying;
        CanPrevious = info.CanPrevious;
        CanNext = info.CanNext;
        if (artChanged)
        {
            Art = IconImageExtensions.DecodeImage(info.Thumbnail);
            HasArt = Art is not null;
            UpdateArtColours(info.Thumbnail);
        }

        HasTimeline = info.Duration > TimeSpan.FromSeconds(1);
        CanSeek = HasTimeline && info.CanSeek;
        if (IsCardOpen)
        {
            ReadAppVolume();
        }
        UpdateProgress();
    }

    /// <summary>The card takes on the cover's colour, as music apps do.</summary>
    private void UpdateArtColours(byte[]? thumbnail)
    {
        ArtTint = null;
        ArtAccent = null;
        if (IconImageExtensions.DecodeImage(thumbnail, 32) is not System.Windows.Media.Imaging.BitmapSource small)
        {
            return;
        }

        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        if (Core.Theming.ColorExtractor.Dominant(pixels) is not { } dominant)
        {
            return;
        }

        var accent = Core.Theming.ColorExtractor.ToAccent(dominant);
        var tint = new LinearGradientBrush(
            Color.FromArgb(0x70, dominant.R, dominant.G, dominant.B),
            Color.FromArgb(0x00, dominant.R, dominant.G, dominant.B),
            new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 1));
        tint.Freeze();
        var accentBrush = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B));
        accentBrush.Freeze();
        ArtTint = tint;
        ArtAccent = accentBrush;
    }

    private void UpdateProgress()
    {
        if (_info is null || !HasTimeline)
        {
            return;
        }

        var position = _info.Position + (_info.IsPlaying ? DateTime.UtcNow - _positionStamp : TimeSpan.Zero);
        if (position > _info.Duration)
        {
            position = _info.Duration;
        }

        ProgressPercent = position.TotalSeconds / _info.Duration.TotalSeconds * 100;
        PositionText = Format(position);
        DurationText = Format(_info.Duration);
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static string FriendlySource(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return string.Empty;
        }

        var name = aumid.Split('!')[0].Split('_')[0];
        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        var dot = name.LastIndexOf('.');
        name = dot >= 0 ? name[(dot + 1)..] : name;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    [RelayCommand]
    private Task PlayPause() => _media.PlayPauseAsync();

    [RelayCommand]
    private Task Next() => _media.NextAsync();

    [RelayCommand]
    private Task Previous() => _media.PreviousAsync();

    public override void Dispose()
    {
        _media.Changed -= OnMediaChanged;
        _progress.Stop();
        base.Dispose();
    }
}
