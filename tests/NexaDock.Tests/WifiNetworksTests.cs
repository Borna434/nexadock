using System.Xml.Linq;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Network;

namespace NexaDock.Tests;

public class WifiNetworksTests
{
    private static WifiNetwork N(string ssid, int signal, bool connected = false, string? profile = null, WifiSecurity security = WifiSecurity.Personal) =>
        new(ssid, signal, security, connected, profile);

    [Fact]
    public void Merge_folds_profile_and_plain_entries_into_one_row()
    {
        var rows = WifiNetworks.Merge([N("Home", 40), N("Home", 70, connected: true, profile: "Home"), N("Cafe", 90)]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Home", rows[0].Ssid); // connected first
        Assert.True(rows[0].IsConnected);
        Assert.Equal("Home", rows[0].ProfileName);
        Assert.Equal(70, rows[0].SignalPercent);
        Assert.Equal("Cafe", rows[1].Ssid);
    }

    [Fact]
    public void Merge_drops_hidden_networks_and_sorts_by_signal()
    {
        var rows = WifiNetworks.Merge([N(string.Empty, 99), N("Weak", 20), N("Strong", 80)]);

        Assert.Equal(["Strong", "Weak"], rows.Select(r => r.Ssid));
    }

    [Theory]
    [InlineData(false, 7, WifiSecurity.Open)]
    [InlineData(true, 1, WifiSecurity.Open)]
    [InlineData(true, 7, WifiSecurity.Personal)] // WPA2-Personal
    [InlineData(true, 9, WifiSecurity.Personal)] // WPA3-Personal
    [InlineData(true, 6, WifiSecurity.Enterprise)] // WPA2-Enterprise
    public void Security_kind_follows_the_auth_algorithm(bool enabled, int auth, WifiSecurity expected) =>
        Assert.Equal(expected, WifiNetworks.SecurityOf(enabled, auth));

    [Fact]
    public void Profile_xml_escapes_name_and_password()
    {
        var xml = WifiNetworks.ProfileXml(N("Tom & Jerry's", 50), "p<a>ss&\"word");
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

        Assert.Equal("Tom & Jerry's", doc.Root!.Element(ns + "name")!.Value);
        Assert.Equal("p<a>ss&\"word", doc.Descendants(ns + "keyMaterial").Single().Value);
        Assert.Equal("WPA2PSK", doc.Descendants(ns + "authentication").Single().Value);
    }

    [Fact]
    public void Open_network_profile_has_no_key()
    {
        var doc = XDocument.Parse(WifiNetworks.ProfileXml(N("Guest", 50, security: WifiSecurity.Open), null));

        Assert.Empty(doc.Descendants().Where(e => e.Name.LocalName == "sharedKey"));
    }
}
