using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NexaDock.Core.Theming;

namespace NexaDock.UI.Services;

/// <summary>
/// The accent colour of the current desktop wallpaper, so the bar matches whatever is behind it.
/// Updated when Windows reports a desktop change and, for slideshows and Spotlight, when the
/// wallpaper file changes (checked once a minute). Reads a 64-pixel copy of the image.
/// </summary>
public sealed class WallpaperAccent : IDisposable
{
    private const int SPI_GETDESKWALLPAPER = 0x73;

    private readonly ILogger<WallpaperAccent> _logger;
    private readonly DispatcherTimer _poll;
    private string? _path;
    private DateTime _stamp;
    private bool _enabled;

    public WallpaperAccent(ILogger<WallpaperAccent> logger)
    {
        _logger = logger;
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _poll.Tick += (_, _) => Refresh(onlyIfChanged: true);
    }

    /// <summary>The accent, or null when off or the wallpaper is a plain colour / unreadable.</summary>
    public Color? Accent { get; private set; }

    public event EventHandler? Changed;

    public void SetEnabled(bool enabled)
    {
        if (enabled == _enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            _poll.Start();
            Refresh(onlyIfChanged: false);
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            _poll.Stop();
            _path = null;
            if (Accent is not null)
            {
                Accent = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)
        {
            _poll.Dispatcher.BeginInvoke(() => Refresh(onlyIfChanged: true));
        }
    }

    private void Refresh(bool onlyIfChanged)
    {
        var path = WallpaperPath();
        var stamp = path is not null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (onlyIfChanged && path == _path && stamp == _stamp)
        {
            return;
        }

        _path = path;
        _stamp = stamp;
        Color? accent = null;
        if (path is not null && File.Exists(path))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = 64;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();

                var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
                bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
                if (ColorExtractor.Dominant(pixels) is { } dominant)
                {
                    var a = ColorExtractor.ToAccent(dominant);
                    accent = Color.FromRgb(a.R, a.G, a.B);
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogDebug(ex, "Wallpaper could not be read.");
            }
        }

        if (accent != Accent)
        {
            Accent = accent;
            _logger.LogInformation("Wallpaper accent: {Accent}.", accent?.ToString() ?? "none");
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string? WallpaperPath()
    {
        var buffer = new char[520];
        return SystemParametersInfo(SPI_GETDESKWALLPAPER, buffer.Length, buffer, 0)
            ? new string(buffer).TrimEnd('\0') is { Length: > 0 } p ? p : null
            : null;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _poll.Stop();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int action, int param, char[] buffer, int winIni);
}
