using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;

namespace NexaDock.Windows.Services;

/// <summary>Network byte counters, drive space and the Recycle Bin (all documented APIs).</summary>
public sealed class StorageNetworkMonitor : IStorageNetworkMonitor
{
    private readonly ILogger<StorageNetworkMonitor> _logger;

    public StorageNetworkMonitor(ILogger<StorageNetworkMonitor> logger) => _logger = logger;

    public NetworkTotals GetNetworkTotals()
    {
        long rx = 0, tx = 0;
        var names = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
                || nic.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var stats = nic.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
                names.Add(nic.Name);
            }
            catch (NetworkInformationException)
            {
            }
        }

        return new NetworkTotals(rx, tx, string.Join(", ", names));
    }

    public IReadOnlyList<DriveUsage> GetDrives()
    {
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var list = new List<DriveUsage>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady)
                {
                    continue;
                }

                list.Add(new DriveUsage(
                    d.Name.TrimEnd('\\'),
                    string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel,
                    (ulong)d.AvailableFreeSpace,
                    (ulong)d.TotalSize,
                    string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return list.OrderByDescending(d => d.IsSystem).ThenBy(d => d.Name).ToList();
    }

    public RecycleBinState GetRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? new RecycleBinState(info.i64NumItems, info.i64Size) : new RecycleBinState(0, 0);
    }

    public void OpenRecycleBin() => Open("shell:RecycleBinFolder");

    public void OpenThisPc() => Open("shell:MyComputerFolder");

    public void EmptyRecycleBin()
    {
        // No SHERB_NOCONFIRMATION: Windows asks the user before anything is deleted.
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0);
        if (hr != 0 && hr != unchecked((int)0x8000FFFF)) // E_UNEXPECTED = bin already empty
        {
            _logger.LogInformation("Empty Recycle Bin returned 0x{Hr:X8}.", hr);
        }
    }

    private void Open(string target)
    {
        try
        {
            Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.ASFW_ANY);
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not open {Target}.", target);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
