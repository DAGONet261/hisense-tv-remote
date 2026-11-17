using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using HisenseTvRemote.Core.Storage;

namespace HisenseTvRemote.Core.Vidaa;

/// <summary>
/// Moteur C# qui parle directement à la TV en MQTT/TLS (portage de pyvidaa). Les commandes s'écrivent
/// comme en ligne de commande (<c>--tv salon key home</c>) et produisent les mêmes textes, repris tels
/// quels dans le journal. Garde une connexion MQTT par TV, fermée après <see cref="IdleTimeout"/>
/// d'inactivité. TV et jetons : <see cref="ITvConfigStore"/>, <see cref="ITokenStore"/> (base SQLite).
/// </summary>
public sealed class NativeTv : ITvEngine
{
    /// <summary>Préfixe des commandes dans le journal.</summary>
    public const string Label = "tv";
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<string?> _certDir;
    private readonly Func<ITvConfigStore> _tvs;
    private readonly Func<ITokenStore> _tokens;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (VidaaTarget Target, VidaaClient Client)> _clients = [];
    private readonly Timer _idleTimer;
    private X509Certificate2? _cert;
    private DateTime _lastUse = DateTime.UtcNow;

    /// <param name="certDirectory">Dossier du certificat client (null = <see cref="ClientCertificate.DefaultDirectory"/>).</param>
    public NativeTv(Func<ITvConfigStore> tvs, Func<ITokenStore> tokens, Func<string?>? certDirectory = null)
    {
        _tvs = tvs;
        _tokens = tokens;
        _certDir = certDirectory ?? (() => null);
        _idleTimer = new Timer(_ => _ = CloseIdleAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>Dernier résultat de <c>discover</c> (avec les MAC Ethernet/Wi-Fi), pour « Retrouver l'IP ».</summary>
    public IReadOnlyList<DiscoveredTv> LastDiscovered { get; private set; } = [];

    private readonly Dictionary<string, IReadOnlyList<JsonObject>> _apps = [];
    private readonly Dictionary<string, IReadOnlyList<JsonObject>> _sources = [];

    /// <summary>Dernière liste d'apps reçue pour cette TV (champs bruts : name, appId, httpIcon…).</summary>
    public IReadOnlyList<JsonObject>? AppsOf(string? alias) => _apps.GetValueOrDefault(alias ?? "");

    /// <summary>Dernière liste de sources reçue pour cette TV (sourceid, sourcename, displayname…).</summary>
    public IReadOnlyList<JsonObject>? SourcesOf(string? alias) => _sources.GetValueOrDefault(alias ?? "");

    private ITokenStore Store => _tokens();

    private static readonly HashSet<string> Supported =
    [
        "key", "keys", "power", "volume", "vol", "app", "source", "input", "status", "wake", "on", "off",
        "auth", "config", "discover", "scan", "info", "text", "listen", "raw",
    ];

    // ----------------------------------------------------------- certificat client --------

    /// <summary>Gardé en mémoire une fois chargé ; tant qu'il manque, on le recherche à chaque connexion
    /// (il peut être importé pendant que l'app tourne).</summary>
    private X509Certificate2? Certificate(StringBuilder err)
    {
        if (_cert is not null) return _cert;
        if (ClientCertificate.Find(_certDir()) is not { } paths)
        {
            err.AppendLine("Certificat client VIDAA introuvable (vidaa_client.pem/.key) : connexion sans certificat.");
            return null;
        }
        try { _cert = ClientCertificate.Load(paths); }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            err.AppendLine("Certificat client illisible : " + ex.Message);
        }
        return _cert;
    }

    // ----------------------------------------------------------- exécution ----------------

    /// <summary>
    /// Exécute une commande (<c>--tv alias commande …</c>). null = commande non prise en charge
    /// (options globales autres que --tv, <c>monitor</c>, <c>sniff</c>…).
    /// </summary>
    public async Task<CommandResult?> TryRunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        string? alias = null;
        var i = 0;
        while (i < args.Count && args[i].StartsWith("--", StringComparison.Ordinal))
        {
            if (args[i] != "--tv" || i + 1 >= args.Count) return null;
            alias = args[i + 1];
            i += 2;
        }
        if (i >= args.Count) return null;
        var cmd = args[i];
        var rest = args.Skip(i + 1).ToList();
        if (rest.Any(a => a is "-h" or "--help")) return null;
        if (!Supported.Contains(cmd)) return null;                  // monitor, sniff, nav… : non portés
        if (cmd == "auth" && rest.FirstOrDefault() == "pair") return null; // passe par StartPairing

        var started = DateTime.Now;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var o = new StringBuilder();
        var e = new StringBuilder();
        int code;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _lastUse = DateTime.UtcNow;
            code = await DispatchAsync(alias, cmd, rest, o, e, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            e.AppendLine($"{ex.GetType().Name}: {ex.Message}");
            code = 1;
        }
        finally
        {
            _lastUse = DateTime.UtcNow;
            _gate.Release();
        }
        return new CommandResult(Label, args.ToList(), code, o.ToString(), e.ToString(), sw.Elapsed, started);
    }

    private async Task<int> DispatchAsync(string? alias, string cmd, List<string> a, StringBuilder o, StringBuilder e,
        CancellationToken ct)
    {
        string Arg(int n) => n < a.Count ? a[n] : "";
        switch (cmd)
        {
            case "config": return await ConfigAsync(alias, a, o, e).ConfigureAwait(false);
            case "discover" or "scan": return await DiscoverAsync(o, e, ct).ConfigureAwait(false);
            case "keys":
                o.AppendLine("Available keys:");
                foreach (var k in VidaaCatalog.AllKeys.Order()) o.AppendLine($"  {k[4..].ToLowerInvariant(),-20} ({k})");
                return 0;
        }

        var tv = TvLookup.Resolve(_tvs().Load(), alias);
        if (tv is null)
        {
            e.AppendLine(alias is null
                ? "No default TV configured. Use 'tv config add' to add a TV."
                : $"TV '{alias}' not found. Use 'tv config list' to see available TVs.");
            return 1;
        }

        switch (cmd)
        {
            case "auth" when Arg(0) is "status" or "":
                return AuthStatus(tv, o);
            case "auth" when Arg(0) == "clear":
                await DropClientAsync(tv).ConfigureAwait(false);
                o.AppendLine(Store.Delete(tv.Host!, tv.Port, tv.Mac) ? "Stored credentials cleared." : "No stored credentials to clear.");
                return 0;
            case "wake":
                return await WakeAsync(tv, o, e).ConfigureAwait(false);
            case "on":
                return await OnAsync(tv, o, e, ct).ConfigureAwait(false);
            case "key" when VidaaCatalog.ResolveKey(Arg(0)) is null:
                e.AppendLine($"Unknown key '{Arg(0)}'. Use 'tv keys' to list available keys.");
                return 1;
        }

        if (cmd == "status") o.AppendLine($"Connecting to {tv.Host}...");
        var client = await ConnectedClientAsync(tv, e, TimeSpan.FromSeconds(cmd == "auth" ? 10 : 5), ct).ConfigureAwait(false);
        if (client is null)
        {
            e.AppendLine("Failed to connect to TV");
            return 1;
        }

        switch (cmd)
        {
            case "key":
            {
                var key = VidaaCatalog.ResolveKey(Arg(0))!;
                return Sent(await client.SendKeyAsync(key).ConfigureAwait(false), $"Sent: {key}", o, e);
            }
            case "listen":
            {
                // listen [secondes] : tout ce qui passe par la TV pendant la durée (diagnostic).
                var seconds = int.TryParse(Arg(0), out var n) ? Math.Clamp(n, 5, 300) : 60;
                o.AppendLine("Abonnements : " + await client.SubscribeAllAsync().ConfigureAwait(false));
                var seen = new List<string>();
                void Log(string topic, string? payload)
                {
                    lock (seen) seen.Add($"{DateTime.Now:HH:mm:ss.fff}  {topic}  {payload}");
                }
                client.MessageObserved += Log;
                try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false); }
                finally { client.MessageObserved -= Log; }
                lock (seen)
                {
                    o.AppendLine($"{seen.Count} message(s) en {seconds} s :");
                    foreach (var line in seen) o.AppendLine(line);
                }
                // L'abonnement à « # » reste sinon actif : on repart d'une connexion propre.
                await DropClientAsync(tv).ConfigureAwait(false);
                return 0;
            }
            case "raw":
            {
                // raw <sujet, {client} remplacé> [contenu] : message brut, pour les essais de protocole.
                if (!Arg(0).StartsWith('/')) return Fail(e, "Sujet attendu (ex. /remoteapp/tv/ui_service/{client}/actions/…)");
                return Sent(await client.PublishAsync(Arg(0), Arg(1)).ConfigureAwait(false),
                    $"Published to {VidaaTopics.For(Arg(0), client.ClientId)}: {Arg(1)}", o, e);
            }
            case "text":
            {
                // text <insert|replace|clear> [texte]
                var action = Arg(0) is "insert" or "replace" or "clear" ? Arg(0) : "replace";
                var text = action == "clear" ? "" : Arg(1);
                return Sent(await client.SendTextAsync(text, action).ConfigureAwait(false),
                    action == "clear" ? "Text cleared" : $"Text {action}: {text}", o, e);
            }
            case "power":
                return Sent(await client.SendKeyAsync("KEY_POWER").ConfigureAwait(false), "Power command sent", o, e);
            case "volume" or "vol":
                return await VolumeAsync(client, Arg(0), Arg(1), o, e).ConfigureAwait(false);
            case "app" when Arg(0) == "list":
            {
                var apps = await client.GetAppsAsync().ConfigureAwait(false);
                if (apps is null) { o.AppendLine("Could not get app list"); return 0; }
                _apps[alias ?? ""] = apps;
                o.AppendLine("Available apps:");
                foreach (var app in apps) o.AppendLine($"  {app["name"]?.ToString() ?? "Unknown"}");
                return 0;
            }
            case "app":
                return await client.LaunchAppAsync(Arg(0)).ConfigureAwait(false)
                    ? Line(o, $"Launching: {Arg(0)}")
                    : Fail(e, $"Failed to launch: {Arg(0)}");
            case "source" or "input" when Arg(0) == "list":
            {
                var sources = await client.GetSourcesAsync().ConfigureAwait(false);
                if (sources is null) { o.AppendLine("Could not get source list"); return 0; }
                _sources[alias ?? ""] = sources;
                o.AppendLine("Available sources:");
                foreach (var s in sources)
                    o.AppendLine($"  {s["sourcename"]?.ToString() ?? s["name"]?.ToString() ?? "Unknown"} (id: {s["sourceid"]?.ToString() ?? "?"})");
                return 0;
            }
            case "source" or "input":
                return Sent(await client.SetSourceAsync(Arg(0)).ConfigureAwait(false), $"Switching to: {Arg(0)}", o, e);
            case "status":
            {
                o.AppendLine("Connected!");
                var state = await client.GetStateAsync().ConfigureAwait(false);
                if (state is null) { o.AppendLine("Could not get state (TV may be off)"); return 0; }
                o.AppendLine("TV State:");
                foreach (var (k, v) in state) o.AppendLine($"  {k}: {v}");
                return 0;
            }
            case "off":
            {
                var (ok, msg) = await client.PowerOffAsync().ConfigureAwait(false);
                return ok ? Line(o, msg) : Fail(e, msg);
            }
            case "auth" when Arg(0) == "refresh":
                return await client.RefreshTokenAsync().ConfigureAwait(false)
                    ? Line(o, "Token refreshed successfully!")
                    : Fail(e, "Token refresh failed.");
            case "info":
            {
                foreach (var (name, topic, resp) in new[]
                         {
                             ("capability", VidaaTopics.GetCapability, VidaaTopics.CapabilityResponse),
                             ("tvinfo", VidaaTopics.GetTvInfo, VidaaTopics.TvInfoResponse),
                             ("deviceinfo", VidaaTopics.GetDeviceInfo, VidaaTopics.DeviceInfoResponse),
                         })
                {
                    o.AppendLine($"== {name}");
                    o.AppendLine((await client.GetInfoAsync(topic, resp).ConfigureAwait(false))?.ToJsonString(
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? "(pas de réponse)");
                }
                return 0;
            }
        }
        e.AppendLine($"Commande non prise en charge par le moteur natif : {cmd}");
        return 1;
    }

    private static int Line(StringBuilder o, string s) { o.AppendLine(s); return 0; }
    private static int Fail(StringBuilder e, string s) { e.AppendLine(s); return 1; }
    private static int Sent(bool ok, string message, StringBuilder o, StringBuilder e) =>
        ok ? Line(o, message) : Fail(e, "Command not sent (TV not connected)");

    private static async Task<int> VolumeAsync(VidaaClient c, string action, string amountText, StringBuilder o, StringBuilder e)
    {
        var amount = int.TryParse(amountText, out var n) ? n : 1;
        switch (action)
        {
            case "up" or "down":
                var key = action == "up" ? "KEY_VOLUMEUP" : "KEY_VOLUMEDOWN";
                for (var i = 0; i < amount; i++)
                {
                    if (!await c.SendKeyAsync(key).ConfigureAwait(false)) return Fail(e, "Command not sent (TV not connected)");
                    if (i < amount - 1) await Task.Delay(100).ConfigureAwait(false);
                }
                return Line(o, $"Volume {action} x{amount}");
            case "mute":
                return Sent(await c.SendKeyAsync("KEY_MUTE").ConfigureAwait(false), "Mute toggled", o, e);
            case "set":
                return Sent(await c.SetVolumeAsync(amount).ConfigureAwait(false), $"Volume set to {amount}", o, e);
            case "get":
                return Line(o, await c.GetVolumeAsync().ConfigureAwait(false) is { } vol ? $"Volume: {vol}" : "Could not get volume");
        }
        return Fail(e, $"Unknown volume action '{action}'");
    }

    private int AuthStatus(TvEntry tv, StringBuilder o)
    {
        var t = Store.Find(tv.Host!, tv.Port, tv.Mac);
        if (t is null) return Line(o, "No stored credentials. Run 'tv auth pair' to authenticate.");
        o.AppendLine("Authentication status:");
        if (t.IsTokenless) return Line(o, "  Paired (this TV issues no token; the client id is authorized).");
        o.AppendLine(t.AccessValid
            ? $"  Access token:  valid (expires in {t.AccessExpiresIn / 3600}h {t.AccessExpiresIn % 3600 / 60}m)"
            : "  Access token:  EXPIRED");
        o.AppendLine(t.RefreshValid
            ? $"  Refresh token: valid (expires in {t.RefreshExpiresIn / 86400}d {t.RefreshExpiresIn % 86400 / 3600}h)"
            : "  Refresh token: EXPIRED");
        if (t.NeedsRefresh) o.AppendLine("\n  Token will auto-refresh on next connection.");
        else if (t.NeedsReauth) o.AppendLine("\n  Both tokens expired. Run 'tv auth pair' to re-authenticate.");
        return 0;
    }

    private static async Task<int> WakeAsync(TvEntry tv, StringBuilder o, StringBuilder e)
    {
        if (tv.Mac is null)
            return Fail(e, "No MAC address specified.\nOr configure a TV first: tv config add <ip>");
        o.AppendLine($"Sending Wake-on-LAN to {tv.Mac}...");
        if (!await WakeOnLan.WakeAsync(tv.Mac, tv.Host).ConfigureAwait(false)) return Fail(e, "Failed to send wake packet");
        o.AppendLine("Magic packet sent!");
        return Line(o, "TV should wake up in a few seconds...");
    }

    private async Task<int> OnAsync(TvEntry tv, StringBuilder o, StringBuilder e, CancellationToken ct)
    {
        if (tv.Mac is not null)
        {
            o.AppendLine($"Sending Wake-on-LAN to {tv.Mac}...");
            await WakeOnLan.WakeAsync(tv.Mac, tv.Host).ConfigureAwait(false);
            o.AppendLine("Waiting for TV to boot...");
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (await ConnectedClientAsync(tv, new StringBuilder(), TimeSpan.FromSeconds(3), ct).ConfigureAwait(false) is { } c)
            {
                if (await c.PowerOnAsync().ConfigureAwait(false)) o.AppendLine("TV is on!");
                return 0;
            }
            if (attempt < 5)
            {
                o.AppendLine($"Waiting... ({attempt + 1}/6)");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }
        return Fail(e, "TV did not respond. It may need WoL enabled in settings.");
    }

    // ----------------------------------------------------------- config / découverte -----

    private async Task<int> ConfigAsync(string? alias, List<string> a, StringBuilder o, StringBuilder e)
    {
        var store = _tvs();
        var action = a.FirstOrDefault() ?? "show";
        var value = a.Skip(1).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
        var aliasIdx = a.IndexOf("--alias");
        var newAlias = aliasIdx >= 0 && aliasIdx + 1 < a.Count ? a[aliasIdx + 1] : null;
        var tvs = store.Load();
        switch (action)
        {
            case "show" or "list":
                if (tvs.Count == 0) return Line(o, "No TVs configured. Use 'tv config add <ip>' to add a TV.");
                o.AppendLine("Configured TVs:");
                foreach (var t in tvs)
                    if (action == "list") o.AppendLine($"  {t.Id}{(t.Alias is null ? "" : $" ({t.Alias})")}");
                    else
                    {
                        o.AppendLine($"\n  {t.Id}{(t.Alias is null ? "" : $" [{t.Alias}]")}{(t.IsDefault ? " (default)" : "")}{(t.Name is null ? "" : " - " + t.Name)}");
                        o.AppendLine($"    Host:  {t.Host}");
                        o.AppendLine($"    Port:  {t.Port}");
                        if (t.Mac is not null) o.AppendLine($"    MAC:   {t.Mac}");
                    }
                return 0;
            case "add":
            {
                if (value is null) return Fail(e, "Please provide IP address: tv config add 192.168.1.100");
                var d = await VidaaDiscovery.ProbeAsync(value).ConfigureAwait(false);
                store.AddTv(value, newAlias, d?.Name, d?.MacEthernet ?? d?.Mac, d?.MacWifi, d?.Brand, d?.ProtocolVersion);
                o.AppendLine($"Added TV at {value}");
                if (d?.Name is not null) o.AppendLine($"  Name:  {d.Name}");
                o.AppendLine(d?.Mac is not null ? $"  MAC:   {d.MacEthernet ?? d.Mac}" : "  MAC:   (not found - TV may be off; re-run when it is on)");
                if (newAlias is not null) o.AppendLine($"  Alias: {newAlias}");
                var def = store.Load().FirstOrDefault(t => t.IsDefault);
                return Line(o, $"  Default TV: {def?.Key ?? "(none)"}");
            }
            case "set-default":
                if (value is null) return Fail(e, "Please provide TV ID or alias");
                return store.SetDefault(value) ? Line(o, $"Default TV set to: {value}") : Fail(e, $"TV not found: {value}");
            case "set-alias":
                if (alias is null) return Fail(e, "Please specify TV with --tv");
                if (value is null) return Fail(e, "Please provide alias");
                return store.SetAlias(alias, value) ? Line(o, $"Alias set to: {value}") : Fail(e, $"TV not found: {alias}");
        }
        return Fail(e, $"Unknown config action '{action}'");
    }

    private async Task<int> DiscoverAsync(StringBuilder o, StringBuilder e, CancellationToken ct)
    {
        o.AppendLine("Scanning for Hisense TVs (timeout: 4.0s)...");
        var found = await VidaaDiscovery.DiscoverAsync(TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
        LastDiscovered = found;
        if (found.Count == 0)
        {
            o.AppendLine("No TVs found.");
            return Fail(e, "Make sure the TV is powered on and on the same network.");
        }
        o.AppendLine($"\nFound {found.Count} TV(s):\n");
        foreach (var t in found)
        {
            o.AppendLine($"  {t.Ip}");
            if (t.Name is not null) o.AppendLine($"    Name:  {t.Name}");
            if (t.Model is not null) o.AppendLine($"    Model: {t.Model}");
            if (t.Brand is not null) o.AppendLine($"    Brand: {t.Brand}");
            if (t.MacEthernet is not null) o.AppendLine($"    MAC Ethernet: {t.MacEthernet}");
            if (t.MacWifi is not null) o.AppendLine($"    MAC Wi-Fi:    {t.MacWifi}");
            if (t.ProtocolVersion is not null) o.AppendLine($"    Protocol: {t.ProtocolVersion}");
            o.AppendLine();
        }
        return Line(o, "To add a TV: tv config add <IP>");
    }

    // ----------------------------------------------------------- connexions ---------------

    private static VidaaTarget TargetOf(TvEntry tv) =>
        new(tv.Host!, tv.Port, tv.Mac, tv.Brand, tv.AuthMode, tv.ProtocolVersion);

    /// <summary>Client connecté pour cette TV (réutilisé s'il l'est encore et que la config n'a pas changé).</summary>
    internal async Task<VidaaClient?> ConnectedClientAsync(TvEntry tv, StringBuilder err, TimeSpan timeout, CancellationToken ct)
    {
        var target = TargetOf(tv);
        if (_clients.TryGetValue(tv.Id, out var cached))
        {
            if (cached.Target == target && cached.Client.IsConnected) return cached.Client;
            await cached.Client.DisposeAsync().ConfigureAwait(false);
            _clients.Remove(tv.Id);
        }
        var client = new VidaaClient(target, Store, Certificate(err));
        client.Log += m => err.AppendLine(m);
        if (!await client.ConnectAsync(timeout, ct).ConfigureAwait(false))
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        _clients[tv.Id] = (target, client);
        return client;
    }

    private async Task DropClientAsync(TvEntry tv)
    {
        if (_clients.Remove(tv.Id, out var c)) await c.Client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CloseIdleAsync()
    {
        if (_clients.Count == 0 || DateTime.UtcNow - _lastUse < IdleTimeout) return;
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            foreach (var (_, c) in _clients) await c.Client.DisposeAsync().ConfigureAwait(false);
            _clients.Clear();
        }
        finally { _gate.Release(); }
    }

    // ----------------------------------------------------------- appairage ----------------

    /// <summary>Session d'appairage native (même déroulé et mêmes textes que <c>tv auth pair</c>).</summary>
    public IPairingSession StartPairing(string? alias) => new NativePairingSession(this, alias);

    internal async Task<(VidaaClient? Client, TvEntry? Tv, string Error)> AcquireForPairingAsync(string? alias, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var tv = TvLookup.Resolve(_tvs().Load(), alias);
        if (tv is null)
            return (null, null, alias is null ? "No TV configured. Use 'tv config add <ip>' first." : $"TV '{alias}' not found.");
        var err = new StringBuilder();
        var c = await ConnectedClientAsync(tv, err, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        return (c, tv, err.ToString());
    }

    internal void ReleasePairing()
    {
        _lastUse = DateTime.UtcNow;
        _gate.Release();
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        _gate.Wait(TimeSpan.FromSeconds(5));
        foreach (var (_, c) in _clients) c.Client.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _clients.Clear();
        _cert?.Dispose();
    }
}
