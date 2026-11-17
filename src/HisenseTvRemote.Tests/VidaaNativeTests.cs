using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.Vidaa;
using Microsoft.Data.Sqlite;

namespace HisenseTvRemote.Tests;

/// <summary>Moteur natif : tout ce qui se teste sans TV ni réseau.</summary>
public class VidaaNativeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vidaa-" + Guid.NewGuid().ToString("N"));
    private readonly AppDatabase _db;

    public VidaaNativeTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new AppDatabase(Path.Combine(_dir, AppDatabase.FileName));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Identifiants_dynamiques_identiques_au_vecteur_logcat_de_l_app_VIDAA()
    {
        // Valeurs relevées dans le logcat de l'app officielle (citées par pyvidaa, credentials.py).
        var c = VidaaCredentials.Generate("56:b8:88:4e:f7:19", AuthMethod.Modern, timestamp: 1766974704);
        Assert.Equal("56:b8:88:4e:f7:19$his$256DBF_vidaacommon_001", c.ClientId);
        Assert.Equal("his$6239759786168176024", c.Username);
        Assert.Equal("C3BA44782E18ABF4892AC44D79A622D2", c.Password);
    }

    [Fact]
    public void Identifiants_legacy_statique_et_mac_sans_separateurs()
    {
        var legacy = VidaaCredentials.Generate("56b8884ef719", AuthMethod.Legacy, timestamp: 1766974704);
        Assert.Equal("56:b8:88:4e:f7:19$his$256DBF_vidaacommon_001", legacy.ClientId);
        Assert.Equal("his$1766974704", legacy.Username);
        Assert.Equal(new MqttCredentials("HisenseTvRemote", "hisenseservice", "multimqttservice"),
            VidaaCredentials.Generate(null, AuthMethod.Static));
        Assert.Throws<ArgumentException>(() => VidaaCredentials.Generate(null, AuthMethod.Modern));
        Assert.Equal(AuthMethod.Modern, VidaaCredentials.MethodOrder(3290)[0]);
        Assert.Equal(AuthMethod.Middle, VidaaCredentials.MethodOrder(3100)[0]);
        Assert.Equal(AuthMethod.Static, VidaaCredentials.MethodOrder(2900)[0]);
        Assert.Equal(AuthMethod.Modern, VidaaCredentials.MethodOrder(null)[0]);
    }

    [Fact]
    public void Touches_regle_KEY_et_nom()
    {
        Assert.Equal("KEY_VOLUMEUP", VidaaCatalog.ResolveKey("volumeup"));
        Assert.Equal("KEY_RETURNS", VidaaCatalog.ResolveKey("returns"));
        Assert.Equal("KEY_BACKS", VidaaCatalog.ResolveKey("backs"));   // rembobiner (vérifié le 17/11)
        Assert.Equal("KEY_BACK", VidaaCatalog.ResolveKey("back"));
        Assert.Equal("KEY_0", VidaaCatalog.ResolveKey("0"));
        Assert.Null(VidaaCatalog.ResolveKey("nosuch"));
    }

    [Fact]
    public void Descripteur_UPnP_et_paquet_WoL()
    {
        const string xml = """
            <root xmlns="urn:schemas-upnp-org:device-1-0"><device>
              <friendlyName>TV Salon</friendlyName><modelName>Renderer</modelName>
              <modelDescription>vidaa_support=1
            transport_protocol=3290
            macEthernet=e43bc9f95c7e
            macWifi=386407359652
            brand=his</modelDescription>
            </device></root>
            """;
        var tv = VidaaDiscovery.ParseDescriptor("192.168.88.19", xml)!;
        Assert.Equal(new DiscoveredTv("192.168.88.19", "TV Salon", null, "his", "e4:3b:c9:f9:5c:7e",
            "e4:3b:c9:f9:5c:7e", "38:64:07:35:96:52", 3290), tv);
        Assert.Null(VidaaDiscovery.ParseDescriptor("1.2.3.4",
            xml.Replace("vidaa_support=1", "x=1").Replace("transport_protocol=3290", "")));

        var packet = WakeOnLan.MagicPacket("e4:3b:c9:f9:5c:7e");
        Assert.Equal(102, packet.Length);
        Assert.All(packet[..6], b => Assert.Equal(0xFF, b));
        Assert.Equal(new byte[] { 0xE4, 0x3B, 0xC9, 0xF9, 0x5C, 0x7E }, packet[96..]);
    }

    [Fact]
    public async Task Commandes_sans_reseau_config_et_erreurs()
    {
        var tvs = new SqliteTvConfigStore(_db);
        using var native = new NativeTv(() => tvs, () => new SqliteTokenStore(_db));
        Assert.Null(await native.TryRunAsync(["--tv", "salon", "monitor"]));       // non porté
        Assert.Null(await native.TryRunAsync(["--ip", "1.2.3.4", "key", "ok"]));
        Assert.Null(await native.TryRunAsync(["--tv", "salon", "auth", "pair"]));  // → StartPairing

        Assert.Contains("No TVs configured", (await native.TryRunAsync(["config", "list"]))!.StandardOutput);

        tvs.AddTv("192.168.88.4", "salon", null, "e4:3b:c9:f9:5c:7e", null, null, 3290);
        var list = (await native.TryRunAsync(["config", "list"]))!;
        Assert.Equal("tv config list", list.CommandLine);
        Assert.Contains("192.168.88.4 (salon)", list.StandardOutput);

        var unknown = (await native.TryRunAsync(["--tv", "inconnue", "key", "ok"]))!;
        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("TV 'inconnue' not found", unknown.Summary);

        Assert.Contains("Unknown key 'nosuch'", (await native.TryRunAsync(["--tv", "salon", "key", "nosuch"]))!.StandardError);
        Assert.Contains("No stored credentials", (await native.TryRunAsync(["--tv", "salon", "auth", "status"]))!.StandardOutput);

        Assert.Equal(0, (await native.TryRunAsync(["--tv", "salon", "config", "set-alias", "séjour"]))!.ExitCode);
        Assert.Equal("séjour", tvs.Load().Single().Alias);
    }
}
