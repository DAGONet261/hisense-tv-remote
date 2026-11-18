using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>État du lecteur de la TV (AVTransport).</summary>
public sealed record PlaybackInfo(string State, TimeSpan? Position, TimeSpan? Duration)
{
    public bool IsPlaying => State == "PLAYING";
    public bool IsPaused => State == "PAUSED_PLAYBACK";
    public bool IsStopped => State is "STOPPED" or "NO_MEDIA_PRESENT";
}

public sealed class DlnaException(string message) : Exception(message);

/// <summary>
/// Lecteur DLNA (MediaRenderer UPnP) de la TV : on lui donne l'adresse d'un fichier, il le télécharge et
/// l'affiche. Standard UPnP AV, sans rien de propre à Hisense. Sur la 55A53FXVT : description
/// <c>http://tv:38400/MediaServer/rendererdevicedesc.xml</c>, services AVTransport et RenderingControl.
/// </summary>
public sealed class DlnaRenderer
{
    public const string AvTransport = "urn:schemas-upnp-org:service:AVTransport:1";
    public const string RenderingControl = "urn:schemas-upnp-org:service:RenderingControl:1";
    private const string MediaRendererSt = "urn:schemas-upnp-org:device:MediaRenderer:1";
    /// <summary>Emplacement constaté sur la 55A53FXVT, essayé si la découverte SSDP ne répond pas.</summary>
    private static readonly string[] KnownDescriptions = ["http://{0}:38400/MediaServer/rendererdevicedesc.xml"];

    private readonly HttpClient _http;

    private DlnaRenderer(HttpClient http, string name, Uri avTransport, Uri? renderingControl)
    {
        _http = http;
        Name = name;
        AvTransportUrl = avTransport;
        RenderingControlUrl = renderingControl;
    }

    public string Name { get; }
    public Uri AvTransportUrl { get; }
    public Uri? RenderingControlUrl { get; }

    /// <summary>Trouve le lecteur DLNA de la TV <paramref name="host"/> (SSDP, puis emplacement connu).</summary>
    public static async Task<DlnaRenderer> FindAsync(IPAddress host, HttpClient http, CancellationToken ct = default)
    {
        var candidates = new List<string>();
        try { candidates.AddRange(await SearchAsync(host, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false)); }
        catch (SocketException) { /* pas de multidiffusion : emplacements connus */ }
        candidates.AddRange(KnownDescriptions.Select(f => string.Format(CultureInfo.InvariantCulture, f, host)));

        foreach (var location in candidates.Distinct())
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(4));
                var xml = await http.GetStringAsync(location, cts.Token).ConfigureAwait(false);
                var r = Parse(xml, new Uri(location), http);
                if (r is not null) return r;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        throw new DlnaException($"La TV ({host}) ne répond pas comme lecteur DLNA. Est-elle allumée ?");
    }

    /// <summary>Lit la description UPnP : nom et adresses de commande (relatives à URLBase ou à la description).</summary>
    public static DlnaRenderer? Parse(string descriptionXml, Uri descriptionUrl, HttpClient http)
    {
        var doc = XDocument.Parse(descriptionXml);
        var urlBase = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
        var baseUri = !string.IsNullOrEmpty(urlBase) && Uri.TryCreate(urlBase, UriKind.Absolute, out var b) ? b : descriptionUrl;
        Uri? Control(string serviceType)
        {
            var svc = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "service" &&
                e.Elements().Any(x => x.Name.LocalName == "serviceType" && x.Value.Trim() == serviceType));
            var url = svc?.Elements().FirstOrDefault(x => x.Name.LocalName == "controlURL")?.Value.Trim();
            return url is null ? null : new Uri(baseUri, url);
        }
        var av = Control(AvTransport);
        if (av is null) return null;
        var name = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "friendlyName")?.Value.Trim() ?? "TV";
        return new DlnaRenderer(http, name, av, Control(RenderingControl));
    }

    /// <summary>Recherche SSDP des lecteurs DLNA ; ne garde que ceux de <paramref name="host"/>.</summary>
    private static async Task<List<string>> SearchAsync(IPAddress host, TimeSpan wait, CancellationToken ct)
    {
        var found = new List<string>();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var msg = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\n" +
                                          $"MX: 1\r\nST: {MediaRendererSt}\r\n\r\n");
        var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        await udp.SendAsync(msg, group, ct).ConfigureAwait(false);
        await udp.SendAsync(msg, new IPEndPoint(host, 1900), ct).ConfigureAwait(false);   // en direct aussi
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(wait);
        try
        {
            while (true)
            {
                var res = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (!res.RemoteEndPoint.Address.Equals(host)) continue;
                var text = Encoding.ASCII.GetString(res.Buffer);
                foreach (var line in text.Split("\r\n"))
                    if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) found.Add(line[9..].Trim());
                if (found.Count > 0) break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        return found;
    }

    // ---------- Commandes AVTransport ------------------------------------------------------------

    public Task SetUriAsync(string url, string title, MediaType type, TimeSpan? duration = null, CancellationToken ct = default) =>
        InvokeAsync("SetAVTransportURI",
            $"<CurrentURI>{SecurityElement.Escape(url)}</CurrentURI><CurrentURIMetaData>{SecurityElement.Escape(Didl(url, title, type, duration))}</CurrentURIMetaData>", ct);

    public Task PlayAsync(CancellationToken ct = default) => InvokeAsync("Play", "<Speed>1</Speed>", ct);
    public Task PauseAsync(CancellationToken ct = default) => InvokeAsync("Pause", "", ct);
    public Task StopAsync(CancellationToken ct = default) => InvokeAsync("Stop", "", ct);

    public Task SeekAsync(TimeSpan position, CancellationToken ct = default) =>
        InvokeAsync("Seek", $"<Unit>REL_TIME</Unit><Target>{FormatTime(position)}</Target>", ct);

    public async Task<PlaybackInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var transport = await InvokeAsync("GetTransportInfo", "", ct).ConfigureAwait(false);
        var position = await InvokeAsync("GetPositionInfo", "", ct).ConfigureAwait(false);
        return new PlaybackInfo(Value(transport, "CurrentTransportState") ?? "UNKNOWN",
            ParseTime(Value(position, "RelTime")), ParseTime(Value(position, "TrackDuration")));
    }

    /// <summary>Métadonnées DIDL-Lite du fichier : titre, genre UPnP et protocolInfo (exigés par beaucoup de TV).</summary>
    public static string Didl(string url, string title, MediaType type, TimeSpan? duration = null) =>
        "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
        "xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">" +
        $"<item id=\"1\" parentID=\"0\" restricted=\"1\"><dc:title>{SecurityElement.Escape(title)}</dc:title>" +
        $"<upnp:class>{type.UpnpClass}</upnp:class>" +
        $"<res protocolInfo=\"{SecurityElement.Escape(type.ProtocolInfo)}\"" +
        (duration is { } d && d > TimeSpan.Zero ? $" duration=\"{FormatTime(d)}.{d.Milliseconds:000}\"" : "") +
        $">{SecurityElement.Escape(url)}</res></item></DIDL-Lite>";

    /// <summary>« H:MM:SS » (format UPnP).</summary>
    public static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}");
    }

    /// <summary>« H:MM:SS », « HH:MM:SS.mmm » ; null pour « NOT_IMPLEMENTED » ou une valeur vide.</summary>
    public static TimeSpan? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Trim().Split(':');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return null;
        return TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(sec);
    }

    public static string? Value(XDocument doc, string name) =>
        doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private async Task<XDocument> InvokeAsync(string action, string args, CancellationToken ct)
    {
        var body = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                   "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                   $"<s:Body><u:{action} xmlns:u=\"{AvTransport}\"><InstanceID>0</InstanceID>{args}</u:{action}></s:Body></s:Envelope>";
        using var req = new HttpRequestMessage(HttpMethod.Post, AvTransportUrl)
        {
            Content = new StringContent(body, new UTF8Encoding(false), "text/xml"),
        };
        req.Content.Headers.ContentType!.CharSet = "\"utf-8\"";
        req.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{AvTransport}#{action}\"");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, cts.Token).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new DlnaException($"La TV ne répond pas ({action}) : {ex.Message}"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new DlnaException($"La TV ne répond pas ({action})."); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            XDocument doc;
            try { doc = XDocument.Parse(text); }
            catch (System.Xml.XmlException) { throw new DlnaException($"Réponse illisible de la TV ({action}, HTTP {(int)res.StatusCode})."); }
            if (!res.IsSuccessStatusCode)
            {
                var code = Value(doc, "errorCode");
                var desc = Value(doc, "errorDescription");
                throw new DlnaException($"La TV refuse « {action} » (erreur UPnP {code ?? ((int)res.StatusCode).ToString(CultureInfo.InvariantCulture)}{(desc is null ? "" : " : " + desc)}).");
            }
            return doc;
        }
    }
}
