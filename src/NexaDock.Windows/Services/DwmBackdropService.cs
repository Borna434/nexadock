using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Windows.Interop;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Uses the documented DWM system backdrop (DWMWA_SYSTEMBACKDROP_TYPE, Windows 11 build 22621+).
/// Set NEXADOCK_BACKDROP=fallback to force the translucent fallback for testing.
/// </summary>
public sealed class DwmBackdropService : IBackdropService
{
    public const int MinimumBuild = 22621;
    public const string OverrideVariable = "NEXADOCK_BACKDROP";

    private readonly ILogger<DwmBackdropService> _logger;

    public DwmBackdropService(ILogger<DwmBackdropService> logger)
    {
        _logger = logger;
        var build = Environment.OSVersion.Version.Build;
        var supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumBuild);
        var forcedFallback = string.Equals(Environment.GetEnvironmentVariable(OverrideVariable), "fallback", StringComparison.OrdinalIgnoreCase);

        var description = forcedFallback
            ? "Translucent fallback (forced via NEXADOCK_BACKDROP)"
            : supported
                ? $"Native acrylic backdrop (DWM, build {build})"
                : $"Translucent fallback — system backdrop needs build {MinimumBuild}+, found {build}";

        Capabilities = new BackdropCapabilities(supported && !forcedFallback, build, description);
        PreferredKind = Capabilities.SupportsSystemBackdrop ? BackdropKind.NativeAcrylic : BackdropKind.TranslucentFallback;
    }

    public BackdropCapabilities Capabilities { get; }

    public BackdropKind PreferredKind { get; }

    public void SetRoundedCorners(IntPtr hwnd, bool rounded)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var corner = rounded ? DWMWCP_ROUND : 1 /* DWMWCP_DONOTROUND */;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    public bool TryApply(IntPtr hwnd, BackdropOptions options)
    {
        if (!Capabilities.SupportsSystemBackdrop || hwnd == IntPtr.Zero)
        {
            return false;
        }

        // Extend the (invisible) frame over the whole client area so the backdrop shows through
        // wherever WPF renders transparent pixels.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var hrFrame = DwmExtendFrameIntoClientArea(hwnd, ref margins);

        SetDarkMode(hwnd, options.DarkMode);

        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // The dock paints its own highlight rim; hide the system accent border.
        var border = unchecked((int)DWMWA_COLOR_NONE);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

        var backdrop = DWMSBT_TRANSIENTWINDOW;
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        if (hr < 0 || hrFrame < 0)
        {
            _logger.LogWarning("DWM backdrop rejected (frame 0x{Frame:X8}, backdrop 0x{Backdrop:X8}).", hrFrame, hr);
            return false;
        }

        _logger.LogInformation("Applied DWM acrylic backdrop.");
        return true;
    }

    public void SetDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, Marshal.SizeOf<int>());
    }
}
