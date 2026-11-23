using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;

namespace NexaDock.Windows.Services;

/// <summary>
/// Listens for clipboard changes (AddClipboardFormatListener) and reads text or an image.
/// Content that apps mark as private is skipped: password managers put
/// "ExcludeClipboardContentFromMonitorProcessing" or "CanIncludeInClipboardHistory = 0" on what
/// they copy, the same signals Windows' own clipboard history honours.
/// </summary>
public sealed class ClipboardMonitor : IClipboardMonitor, IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const long MaxImageBytes = 12L * 1024 * 1024;

    private readonly ILogger<ClipboardMonitor> _logger;
    private HwndSource? _window;
    private bool _ignoreNext;

    public ClipboardMonitor(ILogger<ClipboardMonitor> logger) => _logger = logger;

    public event EventHandler<ClipCapture>? Captured;

    public void Start()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new HwndSource(new HwndSourceParameters("NexaDock.Clipboard") { WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        _window.AddHook(WndProc);
        if (!AddClipboardFormatListener(_window.Handle))
        {
            _logger.LogWarning("Clipboard listener could not be added (error {Error}).", Marshal.GetLastWin32Error());
        }
    }

    public void Stop()
    {
        if (_window is null)
        {
            return;
        }

        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
        _window = null;
    }

    public void SetText(string text)
    {
        _ignoreNext = true;
        Retry(() => Clipboard.SetText(text));
    }

    public void SetImage(byte[] png)
    {
        _ignoreNext = true;
        using var stream = new MemoryStream(png);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Retry(() => Clipboard.SetImage(decoder.Frames[0]));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            handled = true;
            if (_ignoreNext)
            {
                _ignoreNext = false; // our own paste
            }
            else
            {
                Read();
            }
        }

        return IntPtr.Zero;
    }

    private void Read()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || IsPrivate(data))
            {
                return;
            }

            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                if (data.GetData(DataFormats.UnicodeText) is string text)
                {
                    Captured?.Invoke(this, new ClipCapture(text, null, 0, 0));
                }

                return;
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                if (stream.Length <= MaxImageBytes)
                {
                    Captured?.Invoke(this, new ClipCapture(null, stream.ToArray(), image.PixelWidth, image.PixelHeight));
                }
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException)
        {
            // Another app holds the clipboard or the data is unreadable: skip this copy.
            _logger.LogDebug(ex, "Clipboard could not be read.");
        }
    }

    private static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore"))
        {
            return true;
        }

        foreach (var format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (data.GetDataPresent(format) && data.GetData(format) is MemoryStream s && s.Length >= 4)
            {
                var bytes = s.ToArray();
                if (BitConverter.ToInt32(bytes, 0) == 0 && format == "CanIncludeInClipboardHistory")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Retry(Action action)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(30);
            }
        }
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
