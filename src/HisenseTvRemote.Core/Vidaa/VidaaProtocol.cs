// Portage C# de pyvidaa 2.3.0 (https://github.com/warrenrees/pyvidaa), licence MIT,
// Copyright (c) Warren Rees. Voir THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;
using System.Text;

namespace HisenseTvRemote.Core.Vidaa;

/// <summary>Constantes du protocole MQTT local VIDAA (pyvidaa/config/constants.py, topics.py).</summary>
public static class VidaaConstants
{
    public const int MqttPort = 36669;
    public const int KeepAliveSeconds = 20;
    public static readonly int[] UpnpPorts = [38400, 18400];

    public const string StaticUsername = "hisenseservice";
    public const string StaticPassword = "multimqttservice";
    /// <summary>Identifiant MQTT des firmwares sans identifiants dynamiques (authentification « statique »).</summary>
    public const string DefaultClientId = "HisenseTvRemote";
    public const string DefaultBrand = "his";

    public const int ProtocolModernThreshold = 3290;
    public const int ProtocolMiddleThreshold = 3000;

    // Issus de libmqttcrypt.so (app VIDAA), via pyvidaa.
    public const string Pattern = "38D65DC30F45109A369A86FCE866A85B";
    public const string ValueSuffixModern = "h!i@s#$v%i^d&a*a";
    public const string ValueSuffixLegacy = "h*i&s%e!r^v0i1c9";
    public const long TimeXorConstant = 0x5698_1477_2b03_a968;

    public const string SleepState = "fake_sleep_0";
}

/// <summary>Sujets MQTT ; <c>{client}</c> est remplacé par l'identifiant client.</summary>
public static class VidaaTopics
{
    public const string SendKey = "/remoteapp/tv/remote_service/{client}/actions/sendkey";
    public const string Auth = "/remoteapp/tv/ui_service/{client}/actions/authenticationcode";
    public const string AuthClose = "/remoteapp/tv/ui_service/{client}/actions/authenticationcodeclose";
    public const string VidaaConnect = "/remoteapp/tv/ui_service/{client}/actions/vidaa_app_connect";
    public const string GetToken = "/remoteapp/tv/platform_service/{client}/data/gettoken";
    public const string GetState = "/remoteapp/tv/ui_service/{client}/actions/gettvstate";
    public const string GetVolume = "/remoteapp/tv/platform_service/{client}/actions/getvolume";
    public const string SetVolume = "/remoteapp/tv/platform_service/{client}/actions/changevolume";
    public const string GetSources = "/remoteapp/tv/ui_service/{client}/actions/sourcelist";
    public const string SetSource = "/remoteapp/tv/ui_service/{client}/actions/changesource";
    public const string GetApps = "/remoteapp/tv/ui_service/{client}/actions/applist";
    public const string LaunchApp = "/remoteapp/tv/ui_service/{client}/actions/launchapp";
    public const string GetTvInfo = "/remoteapp/tv/platform_service/{client}/actions/gettvinfo";
    public const string GetDeviceInfo = "/remoteapp/tv/platform_service/{client}/actions/getdeviceinfo";
    public const string GetCapability = "/remoteapp/tv/ui_service/{client}/actions/capability";

    // Saisie de texte dans le champ actif (recherche YouTube…) : même message sur les deux sujets,
    // l'un pour les champs natifs, l'autre pour le navigateur des apps web. Repris de
    // stevene1919/hisense_vidaa (licence MIT, voir THIRD-PARTY-NOTICES.md).
    public const string TextInput = "/remoteapp/tv/platform_service/{client}/actions/txtinputdata";
    public const string BrowserTextInput = "/remoteapp/tv/platform_service/{client}/actions/bwsinputdata";

    public const string StateBroadcast = "/remoteapp/mobile/broadcast/ui_service/state";
    public const string TvSleep = "/remoteapp/mobile/broadcast/platform_service/actions/tvsleep";
    public const string StateDirect = "/remoteapp/mobile/{client}/ui_service/data/state";
    public const string VolumeBroadcast = "/remoteapp/mobile/broadcast/ui_service/volume";
    public const string VolumeChange = "/remoteapp/mobile/broadcast/platform_service/actions/volumechange";
    public const string SourcesResponse = "/remoteapp/mobile/{client}/ui_service/data/sourcelist";
    public const string AppsResponse = "/remoteapp/mobile/{client}/ui_service/data/applist";
    public const string AuthResponse = "/remoteapp/mobile/{client}/ui_service/data/authentication";
    public const string AuthCodeResponse = "/remoteapp/mobile/{client}/ui_service/data/authenticationcode";
    public const string TokenResponse = "/remoteapp/mobile/{client}/platform_service/data/tokenissuance";
    public const string TvInfoResponse = "/remoteapp/mobile/{client}/platform_service/data/gettvinfo";
    public const string DeviceInfoResponse = "/remoteapp/mobile/{client}/platform_service/data/getdeviceinfo";
    public const string CapabilityResponse = "/remoteapp/mobile/{client}/ui_service/data/capability";
    public const string VidaaConnectResponse = "/remoteapp/mobile/{client}/ui_service/data/vidaa_app_connect";
    public const string HotelMode = "/remoteapp/mobile/broadcast/ui_service/data/hotelmodechange";

    public static string For(string template, string clientId) => template.Replace("{client}", clientId);
}

/// <summary>Schéma d'identifiants MQTT selon la version de protocole de la TV.</summary>
public enum AuthMethod
{
    Static,  // firmware pré-dynamique : login fixe hisenseservice
    Legacy,  // &lt; 3000 : nom d'utilisateur sans XOR, ancien suffixe
    Middle,  // 3000-3285 : XOR, ancien suffixe
    Modern,  // &gt;= 3290 : XOR, nouveau suffixe
}

public sealed record MqttCredentials(string ClientId, string Username, string Password);

/// <summary>Identifiants dynamiques (pyvidaa/credentials.py, rétro-ingénierie de libmqttcrypt.so).</summary>
public static class VidaaCredentials
{
    public static string AuthMethodName(AuthMethod m) => m.ToString().ToLowerInvariant();

    public static AuthMethod? ParseAuthMethod(string? s) =>
        Enum.TryParse<AuthMethod>(s, ignoreCase: true, out var m) ? m : null;

    /// <summary>Ordre d'essai des méthodes, la plus probable d'abord (protocol.py).</summary>
    public static IReadOnlyList<AuthMethod> MethodOrder(int? protocolVersion) => protocolVersion switch
    {
        < VidaaConstants.ProtocolMiddleThreshold => [AuthMethod.Static, AuthMethod.Legacy, AuthMethod.Middle, AuthMethod.Modern],
        < VidaaConstants.ProtocolModernThreshold => [AuthMethod.Middle, AuthMethod.Modern, AuthMethod.Legacy, AuthMethod.Static],
        _ => [AuthMethod.Modern, AuthMethod.Middle, AuthMethod.Legacy, AuthMethod.Static],
    };

    /// <param name="uuid">MAC de la TV telle qu'enregistrée (la casse compte : elle entre dans un MD5).</param>
    public static MqttCredentials Generate(string? uuid, AuthMethod method, long? timestamp = null,
        string brand = VidaaConstants.DefaultBrand, string operation = "vidaacommon",
        string staticClientId = VidaaConstants.DefaultClientId)
    {
        if (method == AuthMethod.Static)
            return new MqttCredentials(staticClientId, VidaaConstants.StaticUsername, VidaaConstants.StaticPassword);
        if (string.IsNullOrEmpty(uuid))
            throw new ArgumentException($"{AuthMethodName(method)} auth needs a MAC address to build a client_id");

        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!uuid.Contains(':') && !uuid.Contains('-') && uuid.Length == 12)
            uuid = string.Join(":", Enumerable.Range(0, 6).Select(i => uuid.Substring(i * 2, 2)));

        var race = Md5Upper($"{VidaaConstants.Pattern}${uuid}")[..6];
        var clientId = $"{uuid}${brand}${race}_{operation}_001";
        var username = method == AuthMethod.Legacy ? $"{brand}${ts}" : $"{brand}${ts ^ VidaaConstants.TimeXorConstant}";
        var suffix = method == AuthMethod.Modern ? VidaaConstants.ValueSuffixModern : VidaaConstants.ValueSuffixLegacy;
        var remainder = Math.Abs(ts).ToString().Sum(c => c - '0') % 10;
        var valueMd5 = Md5Upper($"{brand}{remainder}{suffix}")[..6];
        var password = Md5Upper($"{ts}${valueMd5}");
        return new MqttCredentials(clientId, username, password);
    }

    private static string Md5Upper(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s)));
}

/// <summary>Touches, sources et apps connues (pyvidaa/keys.py, topics.py).</summary>
public static class VidaaCatalog
{
    public static readonly IReadOnlySet<string> AllKeys = new HashSet<string>
    {
        "KEY_POWER", "KEY_UP", "KEY_DOWN", "KEY_LEFT", "KEY_RIGHT", "KEY_OK", "KEY_OK_LONG_PRESS", "KEY_MENU",
        "KEY_RETURNS", "KEY_EXIT", "KEY_HOME", "KEY_VOLUMEUP", "KEY_VOLUMEDOWN", "KEY_MUTE", "KEY_MUTE_LONG_PRESS",
        "KEY_VOICEUP", "KEY_VOICEDOWN", "KEY_PLAY", "KEY_PAUSE", "KEY_STOP", "KEY_FORWARDS", "KEY_BACKS", "KEY_BACK",
        "KEY_0", "KEY_1", "KEY_2", "KEY_3", "KEY_4", "KEY_5", "KEY_6", "KEY_7", "KEY_8", "KEY_9",
        "KEY_CHANNELUP", "KEY_CHANNELDOWN", "KEY_CHANNELDOT", "KEY_RED", "KEY_GREEN", "KEY_YELLOW", "KEY_BLUE",
        "KEY_SUBTITLE", "KEY_INFO", "KEY_LEFTMOUSEKEYS", "KEY_UDDLEFTMOUSEKEYS", "KEY_UDULEFTMOUSEKEYS",
        "KEY_ZOOMIN", "KEY_ZOOMOUT",
    };

    /// <summary>
    /// Même règle que <c>tv key</c> : « KEY_ » + nom en majuscules. Rembobiner = « backs » (KEY_BACKS,
    /// pendant de KEY_FORWARDS, vérifié le 17/11 sur la 55A53FXVT) ; « back » (KEY_BACK, que pyvidaa
    /// présente comme le rembobinage) est sans effet constaté ; le retour arrière est « returns » (KEY_RETURNS).
    /// </summary>
    public static string? ResolveKey(string name)
    {
        var key = name.ToUpperInvariant();
        if (!key.StartsWith("KEY_", StringComparison.Ordinal)) key = "KEY_" + key;
        return AllKeys.Contains(key) ? key : null;
    }

    /// <summary>
    /// Noms courts → identifiants de source tels que la TV les donne dans <c>source list</c>. pyvidaa
    /// envoyait des numéros (hdmi1 → « 3 »…) : la 55A53FXVT les ignore et n'accepte que les identifiants
    /// textuels (« HDMI1 », « TV », « AVS »), vérifié le 17/11. Tout autre identifiant part tel quel.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SourceIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tv"] = "TV", ["av"] = "AVS",
        ["hdmi1"] = "HDMI1", ["hdmi2"] = "HDMI2", ["hdmi3"] = "HDMI3", ["hdmi4"] = "HDMI4",
    };

    public sealed record AppRef(string AppId, string Name, string Url);

    public static readonly IReadOnlyDictionary<string, AppRef> Apps = new Dictionary<string, AppRef>(StringComparer.OrdinalIgnoreCase)
    {
        ["netflix"] = new("1", "Netflix", "netflix"),
        ["youtube"] = new("3", "YouTube", "youtube"),
        ["amazon"] = new("2", "Prime Video", "amazon"),
        ["prime"] = new("2", "Prime Video", "amazon"),
        ["disney"] = new("295", "Disney+", "https://cd-dmgz.bamgrid.com/bbd/hisense_tv/index.html"),
        ["disney+"] = new("295", "Disney+", "https://cd-dmgz.bamgrid.com/bbd/hisense_tv/index.html"),
        ["tubi"] = new("216", "tubi", "https://ott-hisense.tubitv.com"),
    };
}
