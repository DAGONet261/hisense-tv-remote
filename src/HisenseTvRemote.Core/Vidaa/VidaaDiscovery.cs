// Portage C# de pyvidaa/discovery.py, protocol.py et wol.py (licence MIT, voir THIRD-PARTY-NOTICES.md).
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HisenseTvRemote.Core.Vidaa;

/// <summary>TV trouvée sur le réseau (descripteur UPnP de la TV).</summary>
public sealed record DiscoveredTv(string Ip, string? Name, string? Model, string? Brand, string? Mac,
    string? MacEthernet, string? MacWifi, int? ProtocolVersion);

public static partial class VidaaDiscovery
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>
    /// SSDP M-SEARCH (multicast 239.255.255.250:1900) pour trouver les appareils UPnP, puis lecture
    /// du descripteur de chacun : seuls ceux qui se déclarent VIDAA sont gardés (contrairement à
    /// <c>tv discover</c>, qui listait aussi 192.168.88.216).
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredTv>> DiscoverAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var ips = await SsdpSearchAsync(timeout, ct).ConfigureAwait(false);
        var probes = await Task.WhenAll(ips.Select(ip => ProbeAsync(ip, ct))).ConfigureAwait(false);
        return probes.OfType<DiscoveredTv>().OrderBy(t => t.Ip, StringComparer.Ordinal).ToList();
    }

    private static async Task<IReadOnlyCollection<string>> SsdpSearchAsync(TimeSpan timeout, CancellationToken ct)
    {
        var found = new HashSet<string>();
        var local = LocalIps();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var st in new[] { "urn:schemas-upnp-org:device:MediaRenderer:1", "ssdp:all" })
        {
            var msg = Encoding.ASCII.GetBytes(
                $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 3\r\nST: {st}\r\n\r\n");
            try { await udp.SendAsync(msg, target, ct).ConfigureAwait(false); } catch (SocketException) { }
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                var ip = r.RemoteEndPoint.Address.ToString();
                if (!local.Contains(ip) && Encoding.UTF8.GetString(r.Buffer).StartsWith("HTTP", StringComparison.Ordinal))
                    found.Add(ip);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
        return found;
    }

    private static HashSet<string> LocalIps() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address.ToString())
            .ToHashSet();

    /// <summary>Lit http://ip:38400 (ou 18400)/MediaServer/rendererdevicedesc.xml ; null si ce n'est pas une TV VIDAA.</summary>
    public static async Task<DiscoveredTv?> ProbeAsync(string ip, CancellationToken ct = default)
    {
        foreach (var port in VidaaConstants.UpnpPorts)
        {
            string xml;
            try { xml = await Http.GetStringAsync($"http://{ip}:{port}/MediaServer/rendererdevicedesc.xml", ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { continue; }
            return ParseDescriptor(ip, xml);
        }
        return null;
    }

    public static DiscoveredTv? ParseDescriptor(string ip, string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }
        XNamespace ns = "urn:schemas-upnp-org:device-1-0";
        var device = doc.Descendants(ns + "device").FirstOrDefault();
        var raw = new Dictionary<string, string>();
        foreach (var line in (device?.Element(ns + "modelDescription")?.Value ?? "").Split('\n'))
        {
            var i = line.IndexOf('=');
            if (i > 0) raw[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        raw.TryGetValue("transport_protocol", out var tp);
        if (raw.GetValueOrDefault("vidaa_support", "0") != "1" && tp is null) return null;

        var model = device?.Element(ns + "modelName")?.Value;
        var eth = FormatMac(raw.GetValueOrDefault("macEthernet"));
        var wifi = FormatMac(raw.GetValueOrDefault("macWifi"));
        return new DiscoveredTv(ip, device?.Element(ns + "friendlyName")?.Value, model == "Renderer" ? null : model,
            raw.GetValueOrDefault("brand"), FormatMac(raw.GetValueOrDefault("mac")) ?? eth ?? wifi, eth, wifi,
            int.TryParse(tp, out var v) ? v : null);
    }

    /// <summary>« e43bc9f95c7e » → « e4:3b:c9:f9:5c:7e » (format attendu par le calcul d'identifiants).</summary>
    public static string? FormatMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        mac = mac.Trim();
        return !mac.Contains(':') && !mac.Contains('-') && mac.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)))
            : mac;
    }

    /// <summary>Version du protocole de transport (choisit le schéma d'identifiants) ; null si inconnue.</summary>
    public static async Task<int?> DetectProtocolAsync(string host, CancellationToken ct = default)
    {
        foreach (var port in VidaaConstants.UpnpPorts)
        {
            try
            {
                var xml = await Http.GetStringAsync($"http://{host}:{port}/MediaServer/rendererdevicedesc.xml", ct).ConfigureAwait(false);
                var m = ProtocolRegex().Match(xml);
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        }
        return null;
    }

    [GeneratedRegex(@"transport_protocol\s*[=:>]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProtocolRegex();
}

public static class WakeOnLan
{
    public static byte[] MagicPacket(string mac)
    {
        var hex = mac.Replace(":", "").Replace("-", "");
        if (hex.Length != 12) throw new ArgumentException($"Invalid MAC address: {mac}");
        var bytes = Convert.FromHexString(hex);
        return [.. Enumerable.Repeat((byte)0xFF, 6), .. Enumerable.Repeat(bytes, 16).SelectMany(b => b)];
    }

    /// <summary>Paquet magique vers 255.255.255.255 et le broadcast du sous-réseau, ports 9 et 7.</summary>
    public static async Task<bool> WakeAsync(string mac, string? host)
    {
        var packet = MagicPacket(mac);
        var targets = new List<string> { "255.255.255.255" };
        if (host is not null && host.LastIndexOf('.') is var i and > 0) targets.Add(host[..i] + ".255");
        using var udp = new UdpClient { EnableBroadcast = true };
        var ok = false;
        foreach (var t in targets)
        foreach (var port in new[] { 9, 7 })
        {
            try { await udp.SendAsync(packet, new IPEndPoint(IPAddress.Parse(t), port)).ConfigureAwait(false); ok = true; }
            catch (SocketException) { }
        }
        return ok;
    }
}
