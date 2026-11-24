using System.Security;
using System.Text;
using NexaDock.Core.Abstractions;

namespace NexaDock.Core.Network;

/// <summary>Pure Wi-Fi helpers: merging the scan list, security kinds and Windows profile XML.</summary>
public static class WifiNetworks
{
    /// <summary>A minimal Windows profile: WPA2/WPA3 personal with a passphrase, or open.</summary>
    public static string ProfileXml(WifiNetwork network, string? password)
    {
        var name = SecurityElement.Escape(network.Ssid);
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(network.Ssid));
        var security = network.Security == WifiSecurity.Open
            ? "<authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>"
            : "<authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>"
              + "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>"
              + SecurityElement.Escape(password ?? string.Empty) + "</keyMaterial></sharedKey>";
        return "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">"
            + "<name>" + name + "</name><SSIDConfig><SSID><hex>" + hex + "</hex><name>" + name + "</name></SSID></SSIDConfig>"
            + "<connectionType>ESS</connectionType><connectionMode>auto</connectionMode>"
            + "<MSM><security>" + security + "</security></MSM></WLANProfile>";
    }

    /// <summary>Folds the API's list (one entry per profile and one without) into one row per network.</summary>
    public static IReadOnlyList<WifiNetwork> Merge(IEnumerable<WifiNetwork> raw) =>
        raw.Where(n => n.Ssid.Length > 0)
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(n => n.IsConnected).ThenByDescending(n => n.ProfileName is not null).First()
                with { SignalPercent = g.Max(n => n.SignalPercent), IsConnected = g.Any(n => n.IsConnected) })
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => n.SignalPercent)
            .ToList();

    public static WifiSecurity SecurityOf(bool enabled, int auth) =>
        !enabled || auth is 1 or 10 ? WifiSecurity.Open
        : auth is 4 or 7 or 9 or 2 or 5 ? WifiSecurity.Personal
        : WifiSecurity.Enterprise;
}
