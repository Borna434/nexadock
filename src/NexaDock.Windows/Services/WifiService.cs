using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Network;

namespace NexaDock.Windows.Services;

/// <summary>
/// Wi-Fi networks through the Native Wifi API (wlanapi.dll). Since Windows 11 24H2 network names
/// are only given to apps that may use location; without it the scan reports
/// <see cref="WifiAccess.LocationDenied"/>. Passwords go straight into a Windows profile and are
/// never stored or logged by NexaDock.
/// </summary>
public sealed class WifiService : IWifiService
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceNotActive = 1062;
    private const int AvailableNetworkSize = 628;
    private const int InterfaceInfoSize = 532;
    private const uint FlagConnected = 1;
    private const uint FlagHasProfile = 2;

    private readonly ILogger<WifiService> _logger;

    public WifiService(ILogger<WifiService> logger) => _logger = logger;

    public IReadOnlyList<ConnectionInfo> GetConnections()
    {
        var result = new List<ConnectionInfo>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0)
                {
                    continue; // virtual switches and VPN helpers without a route out
                }

                var kind = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => ConnectionKind.Wifi,
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => ConnectionKind.Ethernet,
                    _ => ConnectionKind.Other,
                };
                var address = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
                result.Add(new ConnectionInfo(kind, nic.Name, nic.Description, nic.Speed, address));
            }
        }
        catch (NetworkInformationException ex)
        {
            _logger.LogDebug(ex, "Reading network connections failed.");
        }

        return result.OrderBy(c => c.Kind).ToList();
    }

    public Task<WifiScan> GetNetworksAsync(bool rescan) => Task.Run(async () =>
    {
        using var client = Client.Open();
        if (client is null)
        {
            return new WifiScan(WifiAccess.NoAdapter, []);
        }

        var iface = client.FirstInterface();
        if (iface is null)
        {
            return new WifiScan(WifiAccess.NoAdapter, []);
        }

        var id = iface.Value;
        if (rescan)
        {
            var scan = WlanScan(client.Handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (scan == ErrorAccessDenied)
            {
                return new WifiScan(WifiAccess.LocationDenied, []);
            }

            if (scan == 0)
            {
                await Task.Delay(2500).ConfigureAwait(false); // the scan finishes within about two seconds
            }
        }

        return client.ReadNetworks(id);
    });

    public Task<WifiConnectResult> ConnectAsync(WifiNetwork network, string? password) => Task.Run(async () =>
    {
        using var client = Client.Open();
        var iface = client?.FirstInterface();
        if (client is null || iface is null)
        {
            return WifiConnectResult.Failed;
        }

        var id = iface.Value;
        var profile = network.ProfileName;
        var created = false;
        if (profile is null)
        {
            if (network.Security == WifiSecurity.Enterprise)
            {
                return WifiConnectResult.Unsupported; // needs a certificate or sign-in page: Windows handles it
            }

            if (network.Security == WifiSecurity.Personal && string.IsNullOrEmpty(password))
            {
                return WifiConnectResult.NeedsPassword;
            }

            var xml = WifiNetworks.ProfileXml(network, password);
            var set = WlanSetProfile(client.Handle, ref id, 0, xml, null, true, IntPtr.Zero, out _);
            if (set != 0)
            {
                set = WlanSetProfile(client.Handle, ref id, 2 /* per user */, xml, null, true, IntPtr.Zero, out _);
            }

            if (set != 0)
            {
                _logger.LogWarning("Saving the Wi-Fi profile failed ({Code}).", set);
                return WifiConnectResult.Failed;
            }

            profile = network.Ssid;
            created = true;
        }

        var parameters = new WLAN_CONNECTION_PARAMETERS
        {
            wlanConnectionMode = 0, // profile
            strProfile = profile,
            dot11BssType = 1, // infrastructure
        };
        var code = WlanConnect(client.Handle, ref id, ref parameters, IntPtr.Zero);
        if (code != 0)
        {
            _logger.LogInformation("Wi-Fi connect request failed ({Code}).", code);
            return WifiConnectResult.Failed;
        }

        // Windows connects in the background; wait for it (a wrong password takes 10-15 seconds).
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            if (client.IsConnectedTo(id, network.Ssid))
            {
                return WifiConnectResult.Connected;
            }
        }

        if (created)
        {
            WlanDeleteProfile(client.Handle, ref id, profile, IntPtr.Zero); // probably a wrong password
        }

        return WifiConnectResult.Failed;
    });

    public Task DisconnectAsync() => Task.Run(() =>
    {
        using var client = Client.Open();
        if (client?.FirstInterface() is { } iface)
        {
            var id = iface;
            WlanDisconnect(client.Handle, ref id, IntPtr.Zero);
        }
    });

    private sealed class Client : IDisposable
    {
        private Client(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }

        public static Client? Open() =>
            WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) == 0 ? new Client(handle) : null;

        public Guid? FirstInterface()
        {
            if (WlanEnumInterfaces(Handle, IntPtr.Zero, out var list) != 0)
            {
                return null;
            }

            try
            {
                var count = Marshal.ReadInt32(list);
                return count == 0 ? null : Marshal.PtrToStructure<Guid>(list + 8);
            }
            finally
            {
                WlanFreeMemory(list);
            }
        }

        public WifiScan ReadNetworks(Guid id)
        {
            var code = WlanGetAvailableNetworkList(Handle, ref id, 0, IntPtr.Zero, out var list);
            if (code == ErrorAccessDenied)
            {
                return new WifiScan(WifiAccess.LocationDenied, []);
            }

            if (code != 0)
            {
                return new WifiScan(code == ErrorServiceNotActive ? WifiAccess.NoAdapter : WifiAccess.Unavailable, []);
            }

            try
            {
                var count = Marshal.ReadInt32(list);
                var raw = new List<WifiNetwork>(count);
                for (var i = 0; i < count; i++)
                {
                    var item = list + 8 + i * AvailableNetworkSize;
                    var profile = Marshal.PtrToStringUni(item);
                    var ssidLength = Math.Min(32, Marshal.ReadInt32(item, 512));
                    var bytes = new byte[ssidLength];
                    Marshal.Copy(item + 516, bytes, 0, ssidLength);
                    var signal = Marshal.ReadInt32(item, 604);
                    var secured = Marshal.ReadInt32(item, 608) != 0;
                    var auth = Marshal.ReadInt32(item, 612);
                    var flags = (uint)Marshal.ReadInt32(item, 620);
                    raw.Add(new WifiNetwork(
                        Encoding.UTF8.GetString(bytes),
                        Math.Clamp(signal, 0, 100),
                        WifiNetworks.SecurityOf(secured, auth),
                        (flags & FlagConnected) != 0,
                        (flags & FlagHasProfile) != 0 && !string.IsNullOrEmpty(profile) ? profile : null));
                }

                return new WifiScan(WifiAccess.Ok, WifiNetworks.Merge(raw));
            }
            finally
            {
                WlanFreeMemory(list);
            }
        }

        public bool IsConnectedTo(Guid id, string ssid) =>
            ReadNetworks(id).Networks.Any(n => n.IsConnected && n.Ssid == ssid);

        public void Dispose() => WlanCloseHandle(Handle, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_PARAMETERS
    {
        public int wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)] public string strProfile;
        public IntPtr pDot11Ssid;
        public IntPtr pDesiredBssidList;
        public int dot11BssType;
        public uint dwFlags;
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    [DllImport("wlanapi.dll")]
    private static extern int WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr ssid, IntPtr ieData, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanGetAvailableNetworkList(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, IntPtr reserved, out IntPtr networkList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanConnect(IntPtr clientHandle, ref Guid interfaceGuid, ref WLAN_CONNECTION_PARAMETERS parameters, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanDisconnect(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanSetProfile(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, string profileXml,
        string? allUserProfileSecurity, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reasonCode);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanDeleteProfile(IntPtr clientHandle, ref Guid interfaceGuid, string profileName, IntPtr reserved);
}
