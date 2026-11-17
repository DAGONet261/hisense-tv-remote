// Portage C# de pyvidaa/client.py (licence MIT, voir THIRD-PARTY-NOTICES.md).
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Client;
using MQTTnet.Formatter;
using HisenseTvRemote.Core.Storage;

namespace HisenseTvRemote.Core.Vidaa;

/// <summary>Ce qu'il faut savoir d'une TV pour s'y connecter (issu de config.yaml).</summary>
public sealed record VidaaTarget(string Host, int Port = VidaaConstants.MqttPort, string? Mac = null,
    string Brand = VidaaConstants.DefaultBrand, string AuthMode = "auto", int? ProtocolVersion = null);

/// <summary>
/// Client MQTT/TLS natif pour une TV VIDAA. Une instance = une connexion. Ses méthodes ne sont pas
/// prévues pour être appelées en parallèle (<see cref="NativeTv"/> les sérialise).
/// </summary>
public sealed class VidaaClient : IAsyncDisposable
{
    private readonly VidaaTarget _tv;
    private readonly ITokenStore _store;
    private readonly X509Certificate2? _clientCert;
    private readonly IMqttClient _mqtt;
    private readonly object _waitersLock = new();
    private readonly List<Waiter> _waiters = [];

    private string _mqttClientId = "";
    private string _username = "";
    private string _password = "";
    private string? _refreshToken;
    private string? _accessToken;
    private bool _pendingRefresh;
    private bool _usingSaved;
    private JsonObject? _lastState;
    private int _stateUnanswered;

    private sealed record Waiter(Func<string, JsonNode?, bool> Match, TaskCompletionSource<JsonNode?> Tcs);

    public VidaaClient(VidaaTarget tv, ITokenStore store, X509Certificate2? clientCert)
    {
        _tv = tv;
        _store = store;
        _clientCert = clientCert;
        _mqtt = new MqttFactory().CreateMqttClient();
        _mqtt.ApplicationMessageReceivedAsync += e =>
        {
            OnMessage(e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };
    }

    /// <summary>Identifiant client utilisé dans les sujets (<c>{client}</c>).</summary>
    public string ClientId { get; private set; } = VidaaConstants.DefaultClientId;
    public AuthMethod? Method { get; private set; }
    public int? ProtocolVersion { get; private set; }
    public bool IsConnected => _mqtt.IsConnected;
    public string Host => _tv.Host;

    /// <summary>Messages de diagnostic (équivalents des avertissements de pyvidaa).</summary>
    public event Action<string>? Log;
    /// <summary>La TV affiche le PIN d'appairage.</summary>
    public event Action? PinShown;
    /// <summary>Tout message reçu (sujet, contenu brut) — pour l'écoute de diagnostic.</summary>
    public event Action<string, string?>? MessageObserved;

    // ---------------------------------------------------------------- connexion ----------
    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        await PrepareCredentialsAsync(ct).ConfigureAwait(false);
        var rc = await TryConnectAsync(timeout, ct).ConfigureAwait(false);
        if (rc is null)
        {
            if (_pendingRefresh && !await RefreshTokenAsync(timeout).ConfigureAwait(false))
                Log?.Invoke("Token refresh failed, connection may not work");
            return true;
        }
        // Seul un refus d'autorisation (4/5) justifie d'essayer d'autres méthodes, et jamais avec des
        // identifiants enregistrés (la TV les a acceptés une fois : il faut réappairer).
        if (rc is not (MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized)
            || _usingSaved)
            return false;

        foreach (var m in VidaaCredentials.MethodOrder(ProtocolVersion).Where(m => m != Method))
        {
            if (m == AuthMethod.Static && _tv.AuthMode == "dynamic") continue;
            if (m != AuthMethod.Static && string.IsNullOrEmpty(_tv.Mac)) continue;
            Log?.Invoke($"Trying {VidaaCredentials.AuthMethodName(m)} authentication...");
            ApplyMethod(m);
            if (await TryConnectAsync(timeout, ct).ConfigureAwait(false) is null) return true;
        }
        Log?.Invoke("All authentication methods failed");
        return false;
    }

    private async Task PrepareCredentialsAsync(CancellationToken ct)
    {
        _usingSaved = false;
        _pendingRefresh = false;
        var saved = _store.Find(_tv.Host, _tv.Port, _tv.Mac);
        if (saved is { ClientId: { } cid, MqttUsername: { } user })
        {
            ProtocolVersion = saved.ProtocolVersion ?? _tv.ProtocolVersion;
            if (saved.IsTokenless)
            {
                Method = AuthMethod.Static;
                ClientId = cid;
                _mqttClientId = StaticMqttClientId();
                (_username, _password) = (VidaaConstants.StaticUsername, VidaaConstants.StaticPassword);
                _usingSaved = true;
                return;
            }
            if (!saved.NeedsReauth)
            {
                Method = VidaaCredentials.ParseAuthMethod(saved.AuthMethod);
                ClientId = _mqttClientId = cid;
                _username = user;
                _refreshToken = saved.RefreshToken;
                if (saved.NeedsRefresh)
                {
                    _password = saved.RefreshToken!;
                    _pendingRefresh = true;
                    Log?.Invoke("Access token expired. Will refresh...");
                }
                else
                {
                    _password = _accessToken = saved.AccessToken!;
                }
                _usingSaved = true;
                return;
            }
            Log?.Invoke("Stored tokens expired. Need to re-authenticate.");
        }

        AuthMethod method;
        if (_tv.AuthMode == "static") method = AuthMethod.Static;
        else
        {
            ProtocolVersion = _tv.ProtocolVersion ?? await VidaaDiscovery.DetectProtocolAsync(_tv.Host, ct).ConfigureAwait(false);
            if (ProtocolVersion is null) Log?.Invoke("Could not detect protocol. Using modern auth (will try fallback if fails)");
            method = VidaaCredentials.MethodOrder(ProtocolVersion)[0];
            if (method == AuthMethod.Static && _tv.AuthMode == "dynamic") method = AuthMethod.Legacy;
        }
        if (method != AuthMethod.Static && string.IsNullOrEmpty(_tv.Mac))
        {
            Log?.Invoke($"{VidaaCredentials.AuthMethodName(method)} auth needs a MAC address to build a client_id, " +
                        "and none is available; falling back to static credentials.");
            method = AuthMethod.Static;
        }
        ApplyMethod(method);
    }

    private void ApplyMethod(AuthMethod method)
    {
        var creds = VidaaCredentials.Generate(_tv.Mac, method, brand: _tv.Brand);
        Method = method;
        (_username, _password, ClientId) = (creds.Username, creds.Password, creds.ClientId);
        // Statique : la TV autorise l'identifiant de sujet ; l'identifiant MQTT doit juste être unique.
        _mqttClientId = method == AuthMethod.Static ? StaticMqttClientId() : creds.ClientId;
    }

    private static string StaticMqttClientId() => $"{VidaaConstants.DefaultClientId}_{Guid.NewGuid():N}"[..(VidaaConstants.DefaultClientId.Length + 9)];

    /// <summary>null = connecté ; sinon le code de refus (ou Unspecified pour un échec réseau/TLS).</summary>
    private async Task<MqttClientConnectResultCode?> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (_mqtt.IsConnected) await _mqtt.DisconnectAsync().ConfigureAwait(false);
        var tls = new MqttClientTlsOptionsBuilder()
            .UseTls()
            // Certificat serveur auto-signé émis pour 127.0.0.1 : pas de vérification (comme pyvidaa).
            .WithCertificateValidationHandler(_ => true)
            .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13);
        if (_clientCert is not null) tls.WithClientCertificates([_clientCert]);

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_tv.Host, _tv.Port)
            .WithClientId(_mqttClientId)
            .WithCredentials(_username, _password)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(VidaaConstants.KeepAliveSeconds))
            .WithTimeout(timeout)
            .WithTlsOptions(tls.Build())
            .Build();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await _mqtt.ConnectAsync(options, cts.Token).ConfigureAwait(false);
        }
        catch (MqttConnectingFailedException ex)
        {
            Log?.Invoke($"Connection to {_tv.Host}:{_tv.Port} failed with code {(int)ex.ResultCode} ({ex.ResultCode})");
            return ex.ResultCode;
        }
        catch (Exception ex) when (ex is MQTTnet.Exceptions.MqttCommunicationException or OperationCanceledException
                                       or System.Net.Sockets.SocketException or IOException)
        {
            Log?.Invoke($"Failed to connect to {_tv.Host}:{_tv.Port}: {ex.Message}");
            return MqttClientConnectResultCode.UnspecifiedError;
        }

        var topics = new[]
        {
            VidaaTopics.StateBroadcast, VidaaTopics.For(VidaaTopics.StateDirect, ClientId), VidaaTopics.TvSleep,
            VidaaTopics.VolumeBroadcast, VidaaTopics.VolumeChange,
            VidaaTopics.For(VidaaTopics.SourcesResponse, ClientId), VidaaTopics.For(VidaaTopics.AppsResponse, ClientId),
            VidaaTopics.For(VidaaTopics.AuthResponse, ClientId), VidaaTopics.For(VidaaTopics.AuthCodeResponse, ClientId),
            VidaaTopics.For(VidaaTopics.TokenResponse, ClientId), VidaaTopics.For(VidaaTopics.TvInfoResponse, ClientId),
            VidaaTopics.For(VidaaTopics.DeviceInfoResponse, ClientId), VidaaTopics.For(VidaaTopics.CapabilityResponse, ClientId),
            VidaaTopics.For(VidaaTopics.VidaaConnectResponse, ClientId), VidaaTopics.HotelMode,
        };
        var sub = new MqttClientSubscribeOptionsBuilder();
        foreach (var t in topics) sub.WithTopicFilter(t);
        await _mqtt.SubscribeAsync(sub.Build(), ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// Écoute de diagnostic. La 55A53FXVT refuse les jokers (<c>#</c>) : on s'abonne un par un aux sujets
    /// connus et plausibles (diffusion générale et réponses adressées à ce client).
    /// </summary>
    /// <returns>Sujets acceptés / refusés, pour le journal.</returns>
    public async Task<string> SubscribeAllAsync()
    {
        string[] broadcast =
        [
            "ui_service/state", "ui_service/volume", "ui_service/data/hotelmodechange", "ui_service/data/inputdata",
            "ui_service/data/keyboard", "ui_service/data/txtinputdata", "ui_service/data/bwsinputdata",
            "platform_service/actions/volumechange", "platform_service/actions/tvsleep",
            "platform_service/actions/bwsinputdata", "platform_service/actions/txtinputdata",
            "platform_service/data/bwsinputdata", "platform_service/data/txtinputdata",
            "remote_service/data/mouse", "ui_service/data/mouse",
        ];
        string[] mine =
        [
            "ui_service/data/state", "ui_service/data/inputdata", "ui_service/data/keyboard",
            "ui_service/data/txtinputdata", "ui_service/data/bwsinputdata",
            "platform_service/data/txtinputdata", "platform_service/data/bwsinputdata",
            "platform_service/actions/bwsinputdata", "platform_service/actions/txtinputdata",
        ];
        var topics = new[] { "#", "/remoteapp/#" }
            .Concat(broadcast.Select(t => "/remoteapp/mobile/broadcast/" + t))
            .Concat(mine.Select(t => $"/remoteapp/mobile/{ClientId}/" + t));
        var accepted = new List<string>();
        var refused = new List<string>();
        foreach (var t in topics)
        {
            var r = await _mqtt.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(t).Build()).ConfigureAwait(false);
            (r.Items.All(i => (int)i.ResultCode <= 2) ? accepted : refused).Add(t);
        }
        return $"{accepted.Count} acceptés, {refused.Count} refusés ({string.Join(", ", refused)})";
    }

    public async Task DisconnectAsync()
    {
        try { if (_mqtt.IsConnected) await _mqtt.DisconnectAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is MQTTnet.Exceptions.MqttCommunicationException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _mqtt.Dispose();
    }

    // ---------------------------------------------------------------- messages -----------
    private void OnMessage(string topic, string? payload)
    {
        MessageObserved?.Invoke(topic, payload);
        JsonNode? node = null;
        try { node = string.IsNullOrEmpty(payload) ? null : JsonNode.Parse(payload); }
        catch (JsonException) { node = payload is null ? null : JsonValue.Create(payload); }

        if (topic.EndsWith("/ui_service/data/authentication", StringComparison.Ordinal)) PinShown?.Invoke();
        if (topic.Contains("tokenissuance", StringComparison.Ordinal) && node is JsonObject tok) HandleToken(tok);
        if ((topic == VidaaTopics.StateBroadcast || topic == VidaaTopics.For(VidaaTopics.StateDirect, ClientId))
            && node is JsonObject st)
        {
            _lastState = st;
            _stateUnanswered = 0;
        }

        List<Waiter> hits;
        lock (_waitersLock)
        {
            hits = _waiters.Where(w => w.Match(topic, node)).ToList();
            foreach (var w in hits) _waiters.Remove(w);
        }
        foreach (var w in hits) w.Tcs.TrySetResult(node);
    }

    private void HandleToken(JsonObject p)
    {
        if (p["accesstoken"]?.GetValue<string>() is not { Length: > 0 } access) return;
        _accessToken = access;
        _password = access;
        var refresh = p["refreshtoken"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(refresh)) _refreshToken = refresh;
        _pendingRefresh = false;
        _store.Save(_tv.Host, _tv.Port, access, _refreshToken, Days(p, "accesstoken_duration_day", 7),
            Days(p, "refreshtoken_duration_day", 30), _mqttClientId, _username, _tv.Mac,
            Method is { } m ? VidaaCredentials.AuthMethodName(m) : null, ProtocolVersion);
    }

    private static double Days(JsonObject p, string name, double fallback) =>
        p[name] is JsonValue v && (v.TryGetValue<double>(out var d) || double.TryParse(v.ToString(), out d)) ? d : fallback;

    private Task<JsonNode?> Expect(Func<string, JsonNode?, bool> match)
    {
        var w = new Waiter(match, new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_waitersLock) _waiters.Add(w);
        return w.Tcs.Task;
    }

    private void Forget(Task<JsonNode?> task)
    {
        lock (_waitersLock) _waiters.RemoveAll(w => w.Tcs.Task == task);
    }

    private async Task<JsonNode?> WaitAsync(Task<JsonNode?> task, TimeSpan timeout)
    {
        try { return await task.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { Forget(task); return null; }
    }

    public async Task<bool> PublishAsync(string topicTemplate, object? payload = null)
    {
        if (!_mqtt.IsConnected) { Log?.Invoke("Not connected to TV"); return false; }
        var text = payload switch
        {
            null => "",
            string s => s,
            JsonNode n => n.ToJsonString(),
            _ => JsonSerializer.Serialize(payload),
        };
        var msg = new MqttApplicationMessageBuilder().WithTopic(VidaaTopics.For(topicTemplate, ClientId)).WithPayload(text).Build();
        try
        {
            var r = await _mqtt.PublishAsync(msg).ConfigureAwait(false);
            return r.IsSuccess;
        }
        catch (Exception ex) when (ex is MQTTnet.Exceptions.MqttCommunicationException or MQTTnet.Exceptions.MqttClientNotConnectedException)
        {
            Log?.Invoke("Publish failed: " + ex.Message);
            return false;
        }
    }

    private async Task<JsonNode?> RequestAsync(string topic, string responseTemplate, TimeSpan timeout, object? payload = null)
    {
        var response = VidaaTopics.For(responseTemplate, ClientId);
        var wait = Expect((t, _) => t == response);
        if (!await PublishAsync(topic, payload).ConfigureAwait(false)) { Forget(wait); return null; }
        return await WaitAsync(wait, timeout).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- commandes ----------
    public Task<bool> SendKeyAsync(string key) => PublishAsync(VidaaTopics.SendKey, key);

    public Task<bool> SetSourceAsync(string source) =>
        PublishAsync(VidaaTopics.SetSource, new JsonObject
        {
            ["sourceid"] = VidaaCatalog.SourceIds.TryGetValue(source, out var id) ? id : source,
        });

    /// <summary>
    /// Texte pour le champ de saisie actif de la TV. <paramref name="action"/> : insert (ajoute),
    /// replace (remplace tout le champ) ou clear (vide).
    /// </summary>
    public async Task<bool> SendTextAsync(string text, string action = "replace")
    {
        var payload = new JsonObject { ["text"] = text, ["action"] = action };
        var native = await PublishAsync(VidaaTopics.TextInput, payload).ConfigureAwait(false);
        var browser = await PublishAsync(VidaaTopics.BrowserTextInput, payload.DeepClone()).ConfigureAwait(false);
        return native && browser;
    }

    public Task<bool> SetVolumeAsync(int level) =>
        PublishAsync(VidaaTopics.SetVolume, Math.Clamp(level, 0, 100).ToString());

    public async Task<bool> LaunchAppAsync(string name)
    {
        JsonObject app;
        if (VidaaCatalog.Apps.TryGetValue(name, out var known))
            app = new JsonObject { ["appId"] = known.AppId, ["name"] = known.Name, ["url"] = known.Url };
        else
        {
            var list = await GetAppsAsync().ConfigureAwait(false);
            if (list is null) { Log?.Invoke("Could not get app list. Try one of: netflix, youtube, amazon, disney, hulu"); return false; }
            var hit = list.FirstOrDefault(a => string.Equals(a["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (hit is null) { Log?.Invoke($"App '{name}' not found."); return false; }
            app = new JsonObject { ["appId"] = hit["appId"]?.DeepClone(), ["name"] = hit["name"]?.DeepClone(), ["url"] = hit["url"]?.DeepClone() };
        }
        return await PublishAsync(VidaaTopics.LaunchApp, app).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JsonObject>?> GetAppsAsync(TimeSpan? timeout = null) =>
        (await RequestAsync(VidaaTopics.GetApps, VidaaTopics.AppsResponse, timeout ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        is JsonArray a ? a.OfType<JsonObject>().ToList() : null;

    public async Task<IReadOnlyList<JsonObject>?> GetSourcesAsync(TimeSpan? timeout = null) =>
        (await RequestAsync(VidaaTopics.GetSources, VidaaTopics.SourcesResponse, timeout ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        is JsonArray a ? a.OfType<JsonObject>().ToList() : null;

    public async Task<JsonObject?> GetInfoAsync(string topic, string responseTemplate, TimeSpan? timeout = null) =>
        await RequestAsync(topic, responseTemplate, timeout ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false) as JsonObject;

    /// <summary>
    /// État courant. Certains firmwares ne répondent qu'aux changements : après 2 silences on n'attend
    /// plus que 0,3 s et on renvoie le dernier état connu (comme pyvidaa).
    /// </summary>
    public async Task<JsonObject?> GetStateAsync(TimeSpan? timeout = null)
    {
        var direct = VidaaTopics.For(VidaaTopics.StateDirect, ClientId);
        var wait = Expect((t, n) => (t == VidaaTopics.StateBroadcast || t == direct) && n is JsonObject);
        if (!await PublishAsync(VidaaTopics.GetState).ConfigureAwait(false)) { Forget(wait); return _lastState; }
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        if (_stateUnanswered >= 2) limit = TimeSpan.FromMilliseconds(Math.Min(limit.TotalMilliseconds, 300));
        // La 55A53FXVT ne répond pas à gettvstate mais diffuse son état à chaque changement : si on en a
        // déjà reçu un sur cette connexion, il est à jour ; inutile d'attendre 5 s.
        else if (_lastState is not null) limit = TimeSpan.FromMilliseconds(Math.Min(limit.TotalMilliseconds, 1000));
        if (await WaitAsync(wait, limit).ConfigureAwait(false) is JsonObject s) return s;
        _stateUnanswered++;
        return _lastState;
    }

    public async Task<int?> GetVolumeAsync(TimeSpan? timeout = null)
    {
        var wait = Expect((t, n) => (t == VidaaTopics.VolumeBroadcast || t == VidaaTopics.VolumeChange
                                     || t.EndsWith("/volume", StringComparison.Ordinal))
                                    && n is JsonObject o && (o["volume_type"]?.GetValue<int>() ?? 0) == 0);
        if (!await PublishAsync(VidaaTopics.GetVolume).ConfigureAwait(false)) { Forget(wait); return null; }
        if (await WaitAsync(wait, timeout ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false) is not JsonObject o) return null;
        foreach (var f in new[] { "volume_value", "volume", "value" })
            if (o[f] is JsonValue v && int.TryParse(v.ToString(), out var vol)) return vol;
        return null;
    }

    public static bool IsSleeping(JsonObject? state) => state?["statetype"]?.ToString() == VidaaConstants.SleepState;

    /// <summary>Allume si l'état dit « veille » ; KEY_POWER étant une bascule, on vérifie d'abord.</summary>
    public async Task<bool> PowerOnAsync()
    {
        var state = await GetStateAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        return state is not null && !IsSleeping(state) || await SendKeyAsync("KEY_POWER").ConfigureAwait(false);
    }

    /// <summary>
    /// Éteint si la TV est allumée. Sans réponse d'état, rien n'est envoyé : la TV est très
    /// probablement déjà éteinte et KEY_POWER la rallumerait.
    /// </summary>
    public async Task<(bool Ok, string Message)> PowerOffAsync()
    {
        var state = await GetStateAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (state is null) return (false, "TV did not report its state; not sending power off.");
        if (IsSleeping(state)) return (true, "TV is already off.");
        return await SendKeyAsync("KEY_POWER").ConfigureAwait(false) ? (true, "Power off command sent.") : (false, "Power off not sent.");
    }

    // ---------------------------------------------------------------- appairage ----------
    private bool Tokenless => Method is AuthMethod.Static or AuthMethod.Legacy;

    /// <summary>Demande à la TV d'afficher le PIN.</summary>
    public Task<bool> StartPairingAsync() =>
        Tokenless
            ? PublishAsync(VidaaTopics.GetState)
            : PublishAsync(VidaaTopics.VidaaConnect, new JsonObject
            {
                ["app_version"] = 2, ["connect_result"] = 0, ["device_type"] = "Mobile App",
            });

    /// <summary>Envoie le PIN, attend l'acceptation puis le jeton (enregistré dans tokens.json).</summary>
    public async Task<bool> AuthenticateAsync(string pin, TimeSpan? timeout = null)
    {
        if (!int.TryParse(pin.Trim(), out var pinInt)) { Log?.Invoke($"Invalid PIN: {pin}"); return false; }
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var authCode = VidaaTopics.For(VidaaTopics.AuthCodeResponse, ClientId);
        var authResp = VidaaTopics.For(VidaaTopics.AuthResponse, ClientId);
        var accepted = Expect((t, n) => (t == authCode || t == authResp) && n is JsonObject o
                                        && o["result"] is JsonValue r && r.ToString() == "1");
        // Le PIN doit partir en entier JSON, pas en chaîne.
        if (!await PublishAsync(VidaaTopics.Auth, new JsonObject { ["authNum"] = pinInt }).ConfigureAwait(false))
        {
            Forget(accepted);
            return false;
        }
        if (await WaitAsync(accepted, limit).ConfigureAwait(false) is null) return false;

        var token = Expect((t, n) => t.Contains("tokenissuance", StringComparison.Ordinal) && n is JsonObject o && o["accesstoken"] is not null);
        await PublishAsync(VidaaTopics.GetToken, new JsonObject { ["refreshtoken"] = "" }).ConfigureAwait(false);
        await PublishAsync(VidaaTopics.AuthClose, "").ConfigureAwait(false);
        if (await WaitAsync(token, limit).ConfigureAwait(false) is not null) return _accessToken is not null;

        if (Tokenless)
        {
            // Firmware pré-jeton : accepter le PIN EST l'appairage ; on mémorise l'identifiant autorisé.
            _store.Save(_tv.Host, _tv.Port, null, null, 0, 0, ClientId, _username, _tv.Mac, "static", ProtocolVersion);
            return true;
        }
        Log?.Invoke($"PIN accepted but no access token received within {limit.TotalSeconds:0}s");
        return false;
    }

    public async Task<bool> RefreshTokenAsync(TimeSpan? timeout = null)
    {
        if (string.IsNullOrEmpty(_refreshToken)) { Log?.Invoke("No refresh token available"); return false; }
        var token = Expect((t, n) => t.Contains("tokenissuance", StringComparison.Ordinal) && n is JsonObject o && o["accesstoken"] is not null);
        if (!await PublishAsync(VidaaTopics.GetToken, new JsonObject { ["refreshtoken"] = _refreshToken }).ConfigureAwait(false))
        {
            Forget(token);
            return false;
        }
        return await WaitAsync(token, timeout ?? TimeSpan.FromSeconds(10)).ConfigureAwait(false) is not null;
    }
}
