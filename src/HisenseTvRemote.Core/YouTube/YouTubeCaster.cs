using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HisenseTvRemote.Core.YouTube;

public sealed class YouTubeCastException(string message) : Exception(message);

/// <summary>
/// « Caster » une vidéo YouTube sur la TV, comme le bouton de l'app YouTube mobile (vérifié le 18/11 sur
/// la 55A53FXVT) :
/// <list type="number">
/// <item>DIAL (port 3366/3367 de la TV) : YouTube est lancé si besoin et donne son <c>screenId</c> ;</item>
/// <item>serveurs YouTube (API « Lounge ») : jeton de salon pour ce screenId, session de télécommande,
/// puis commande <c>setPlaylist</c> (lire) ou <c>addVideo</c> (ajouter à la file).</item>
/// </list>
/// Lancer YouTube par DIAL avec « v=… » ne suffit pas (la TV reste sur le choix du profil, sans vidéo).
/// Piège : dans la session, la commande prend deux tirets bas (<c>req0__sc</c>), ses paramètres un seul
/// (<c>req0_videoId</c>) — avec deux, le lecteur s'ouvre sur un écran noir.
/// </summary>
public sealed partial class YouTubeCaster(HttpClient http)
{
    private const string Lounge = "https://www.youtube.com/api/lounge";
    /// <summary>Origine exigée par le serveur DIAL de la TV pour YouTube (sinon 403).</summary>
    private const string DialOrigin = "package:com.google.android.youtube";
    private static readonly string[] KnownDialDescriptions = ["http://{0}:3366/dd.xml"];

    private readonly Dictionary<string, (string Token, DateTime Until)> _tokens = [];
    private readonly string _deviceId = "htr" + Guid.NewGuid().ToString("N")[..22];

    /// <summary>Nom affiché sur la TV (« … est connecté »).</summary>
    public string DeviceName { get; init; } = "VIDAA - Hisense (PC)";

    /// <summary>Lit la vidéo sur la TV (remplace ce qui est en cours).</summary>
    public Task PlayAsync(IPAddress tv, string videoId, CancellationToken ct = default) =>
        CommandAsync(tv, ct, new Dictionary<string, string>
        {
            ["req0__sc"] = "setPlaylist", ["req0_videoId"] = videoId, ["req0_listId"] = "",
            ["req0_currentTime"] = "0", ["req0_currentIndex"] = "-1", ["req0_audioOnly"] = "false",
        });

    /// <summary>Ajoute la vidéo à la file de lecture de YouTube sur la TV.</summary>
    public Task QueueAsync(IPAddress tv, string videoId, CancellationToken ct = default) =>
        CommandAsync(tv, ct, new Dictionary<string, string> { ["req0__sc"] = "addVideo", ["req0_videoId"] = videoId });

    private async Task CommandAsync(IPAddress tv, CancellationToken ct, Dictionary<string, string> command)
    {
        var screen = await ScreenIdAsync(tv, ct).ConfigureAwait(false);
        var token = await LoungeTokenAsync(screen, ct).ConfigureAwait(false);
        var (sid, gsession) = await BindAsync(token, ct).ConfigureAwait(false);
        command["count"] = "1";
        using var res = await PostFormAsync($"{Lounge}/bc/bind?SID={Uri.EscapeDataString(sid)}&gsessionid={Uri.EscapeDataString(gsession)}&RID=2&VER=8&CVER=1",
            command, token, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new YouTubeCastException($"YouTube a refusé la demande (HTTP {(int)res.StatusCode}).");
    }

    // ---------- 1. DIAL : YouTube sur la TV et son screenId -------------------------------------

    private async Task<string> ScreenIdAsync(IPAddress tv, CancellationToken ct)
    {
        var app = await AppUrlAsync(tv, ct).ConfigureAwait(false) + "YouTube";
        var xml = await GetDialAsync(app, ct).ConfigureAwait(false);
        if (ParseScreenId(xml) is { } id && xml.Contains("<state>running</state>", StringComparison.Ordinal)) return id;

        // YouTube arrêté : lancement, puis attente du screenId (quelques secondes).
        using (var launch = new HttpRequestMessage(HttpMethod.Post, app) { Content = new StringContent("") })
        {
            launch.Headers.TryAddWithoutValidation("Origin", DialOrigin);
            using var r = await SendAsync(launch, ct).ConfigureAwait(false);
            if (r.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
                throw new YouTubeCastException($"La TV n'a pas pu ouvrir YouTube (HTTP {(int)r.StatusCode}).");
        }
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            if (ParseScreenId(await GetDialAsync(app, ct).ConfigureAwait(false)) is { } started) return started;
        }
        throw new YouTubeCastException("YouTube s'est ouvert sur la TV mais ne répond pas encore : réessayez dans quelques secondes.");
    }

    /// <summary>Adresse des applications DIAL (en-tête « Application-URL » de la description).</summary>
    private async Task<string> AppUrlAsync(IPAddress tv, CancellationToken ct)
    {
        foreach (var f in KnownDialDescriptions)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, string.Format(System.Globalization.CultureInfo.InvariantCulture, f, tv));
            using var res = await SendAsync(req, ct).ConfigureAwait(false);
            if (res.Headers.TryGetValues("Application-URL", out var v) && v.FirstOrDefault() is { } url)
                return url.EndsWith('/') ? url : url + "/";
        }
        throw new YouTubeCastException("La TV ne propose pas DIAL (envoi de vidéos YouTube).");
    }

    private async Task<string> GetDialAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Origin", DialOrigin);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) throw new YouTubeCastException("YouTube n'est pas installé sur la TV.");
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public static string? ParseScreenId(string dialXml) =>
        ScreenIdRegex().Match(dialXml) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    // ---------- 2. Serveurs YouTube : jeton et session ------------------------------------------

    private async Task<string> LoungeTokenAsync(string screenId, CancellationToken ct)
    {
        if (_tokens.TryGetValue(screenId, out var t) && t.Until > DateTime.UtcNow) return t.Token;
        using var res = await PostFormAsync($"{Lounge}/pairing/get_lounge_token_batch",
            new Dictionary<string, string> { ["screen_ids"] = screenId }, null, ct).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var token = ParseLoungeToken(json) ?? throw new YouTubeCastException("YouTube n'a pas fourni de jeton pour la TV.");
        _tokens[screenId] = (token, DateTime.UtcNow.AddHours(1));
        return token;
    }

    public static string? ParseLoungeToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("screens")[0].GetProperty("loungeToken").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<(string Sid, string GSession)> BindAsync(string token, CancellationToken ct)
    {
        using var res = await PostFormAsync($"{Lounge}/bc/bind?RID=1&VER=8&CVER=1", new Dictionary<string, string>
        {
            ["device"] = "REMOTE_CONTROL", ["id"] = _deviceId, ["name"] = DeviceName,
            ["mdx-version"] = "3", ["pairing_type"] = "cast", ["app"] = "android-phone-13.14.55",
        }, token, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseBind(body) ?? throw new YouTubeCastException("YouTube n'a pas ouvert de session avec la TV.");
    }

    /// <summary>Identifiants de session dans la réponse de « bind » (<c>["c","SID",…]</c>, <c>["S","gsessionid"]</c>).</summary>
    public static (string Sid, string GSession)? ParseBind(string body)
    {
        var sid = SidRegex().Match(body);
        var gs = GSessionRegex().Match(body);
        return sid.Success && gs.Success ? (sid.Groups[1].Value, gs.Groups[1].Value) : null;
    }

    // ---------- Liens ------------------------------------------------------------------------------

    /// <summary>
    /// Identifiant (11 caractères) d'une vidéo YouTube : lien watch, youtu.be, Shorts, direct, embed,
    /// music.youtube.com, ou identifiant seul ; null si ce n'est pas un lien de vidéo YouTube.
    /// </summary>
    public static string? ParseVideoId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        if (VideoIdRegex().IsMatch(text)) return text;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        string? id = null;
        if (host is "youtu.be" or "www.youtu.be") id = uri.AbsolutePath.Trim('/').Split('/')[0];
        else if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments[0] == "watch")
                id = System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
            else if (segments.Length >= 2 && segments[0] is "shorts" or "live" or "embed" or "v" or "e")
                id = segments[1];
        }
        return id is not null && VideoIdRegex().IsMatch(id) ? id : null;
    }

    // ---------- HTTP -----------------------------------------------------------------------------

    private Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, string? loungeToken, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (loungeToken is not null) req.Headers.TryAddWithoutValidation("X-YouTube-LoungeId-Token", loungeToken);
        return SendAsync(req, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try { return await http.SendAsync(req, cts.Token).ConfigureAwait(false); }
        catch (HttpRequestException ex)
        {
            throw new YouTubeCastException(req.RequestUri?.Host == "www.youtube.com"
                ? $"YouTube est injoignable (connexion Internet ?) : {ex.Message}"
                : $"La TV ne répond pas (allumée ?) : {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new YouTubeCastException(req.RequestUri?.Host == "www.youtube.com" ? "YouTube ne répond pas." : "La TV ne répond pas (allumée ?).");
        }
    }

    [GeneratedRegex(@"<screenId>([^<]+)</screenId>")]
    private static partial Regex ScreenIdRegex();
    [GeneratedRegex(@"\[""c"",""(.*?)"",""")]
    private static partial Regex SidRegex();
    [GeneratedRegex(@"\[""S"",""(.*?)""\]")]
    private static partial Regex GSessionRegex();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdRegex();
}
