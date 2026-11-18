using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.Vidaa;

namespace HisenseTvRemote.Tests;

/// <summary>Contrôleur, résultats de commande, catalogue des touches, certificat client.</summary>
public class CoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "core-" + Guid.NewGuid().ToString("N"));

    public CoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class NoStore : ITvConfigStore
    {
        public string Location => "mémoire";
        public IReadOnlyList<TvEntry> Load() => [];
        public void AddTv(string ip, string? alias, string? name, string? mac, string? macWifi, string? brand, int? pv) { }
        public bool SetAlias(string aliasOrId, string newAlias) => false;
        public bool SetDefault(string aliasOrId) => false;
        public bool UpdateHost(string id, string newHost) => false;
    }

    [Fact]
    public async Task Controleur_construit_les_commandes_et_les_journalise()
    {
        var engine = new FakeEngine();
        using var tv = new TvController(engine, new NoStore());
        var seen = new List<CommandResult>();
        tv.CommandCompleted += seen.Add;

        var r = await tv.KeyAsync("salon", "home");
        Assert.Equal(["--tv", "salon", "key", "home"], engine.Received[^1]);
        Assert.Equal("tv --tv salon key home", r.CommandLine);
        Assert.Equal("Sent: KEY_HOME", r.Summary);

        await tv.KeyAsync(null, "ok");                                       // TV par défaut : pas de --tv
        Assert.Equal(["key", "ok"], engine.Received[^1]);
        await tv.ConfigSetAliasAsync("salon", "séjour");
        Assert.Equal(["--tv", "salon", "config", "set-alias", "séjour"], engine.Received[^1]);
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public async Task Commande_inconnue_du_moteur_donne_une_erreur_lisible()
    {
        using var tv = new TvController(new FakeEngine(), new NoStore());
        var r = await tv.RunForTvAsync("salon", "monitor");
        Assert.False(r.Success);
        Assert.Contains("Commande non prise en charge", r.Summary);
    }

    [Fact]
    public void Resume_d_une_commande()
    {
        var ok = new CommandResult("tv", [], 0, "Available apps:\n  Netflix\n  YouTube\n", "", TimeSpan.Zero, DateTime.Now);
        Assert.Equal("Available apps: (+2 lignes)", ok.Summary);
        var ko = new CommandResult("tv", [], 1, "", "Connection lost\nFailed to connect to TV\n", TimeSpan.Zero, DateTime.Now);
        Assert.Equal("Failed to connect to TV", ko.Summary);
        Assert.Equal("délai dépassé", (ko with { TimedOut = true }).Summary);
    }

    [Fact]
    public void Catalogue_des_touches()
    {
        Assert.Equal(KeyCatalog.All.Count, KeyCatalog.All.Distinct().Count());
        Assert.Equal(1 + 5 + 4 + 3 + 4 + 10 + 2 + 4 + 14, KeyCatalog.All.Count);
        // « backs » rembobine (KEY_BACKS) : rangée avec la lecture ; « returns » est le retour arrière ;
        // « back » (KEY_BACK) ne fait rien sur la 55A53FXVT : il n'est plus dans la lecture.
        Assert.Contains("backs", KeyCatalog.Playback);
        Assert.DoesNotContain("back", KeyCatalog.Playback);
        Assert.Contains("returns", KeyCatalog.Menu);
        Assert.Equal(("Retour", "Rembobiner", "Accueil", "7"),
            (KeyCatalog.Label("returns"), KeyCatalog.Label("backs"), KeyCatalog.Label("home"), KeyCatalog.Label("7")));
        // Chaque touche (hors chiffres) a un nom français, et la TV la connaît.
        Assert.All(KeyCatalog.All.Where(k => !char.IsDigit(k[0])), k => Assert.True(KeyCatalog.Labels.ContainsKey(k), k));
        Assert.All(KeyCatalog.All, k => Assert.NotNull(VidaaCatalog.ResolveKey(k)));
    }

    [Fact]
    public void Certificat_importe_depuis_un_p12_sans_outil_externe()
    {
        // Un .p12 protégé par mot de passe, comme celui de l'APK VIDAA.
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test-vidaa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var self = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        var p12 = Path.Combine(_dir, "client.p12");
        File.WriteAllBytes(p12, self.Export(X509ContentType.Pkcs12, "secret"));
        var target = Path.Combine(_dir, "certs");

        Assert.ThrowsAny<CryptographicException>(() => ClientCertificate.ImportPkcs12(p12, "faux", target));

        Assert.Equal("CN=test-vidaa", ClientCertificate.ImportPkcs12(p12, "secret", target));
        var paths = ClientCertificate.Find(target)!.Value;
        Assert.StartsWith(target, paths.Cert);
        using var loaded = ClientCertificate.Load(paths);
        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(self.Thumbprint, loaded.Thumbprint);

        // Certificat intégré à l'app : installé si absent, mis à jour s'il change, jamais rétrogradé.
        var bundledDir = Path.Combine(_dir, "bundled");
        var bytes = File.ReadAllBytes(p12);
        Assert.Equal(ClientCertificate.BundledResult.Installed, ClientCertificate.InstallBundled(bytes, "secret", bundledDir));
        Assert.Equal(ClientCertificate.BundledResult.AlreadyInstalled, ClientCertificate.InstallBundled(bytes, "secret", bundledDir));
        using var newer = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        var newerBytes = newer.Export(X509ContentType.Pkcs12, "neuf");
        Assert.Equal(ClientCertificate.BundledResult.Updated, ClientCertificate.InstallBundled(newerBytes, "neuf", bundledDir));
        using (var now = ClientCertificate.Load(ClientCertificate.Find(bundledDir)!.Value)) Assert.Equal(newer.Thumbprint, now.Thumbprint);
        Assert.Equal(ClientCertificate.BundledResult.KeptNewer, ClientCertificate.InstallBundled(bytes, "secret", bundledDir));
        Assert.ThrowsAny<CryptographicException>(() => ClientCertificate.InstallBundled(bytes, "faux", Path.Combine(_dir, "x")));

        // Réimport d'une paire PEM existante.
        var other = Path.Combine(_dir, "certs2");
        Assert.Equal("CN=test-vidaa", ClientCertificate.ImportPem(paths.Cert, paths.Key, other));
        Assert.NotNull(ClientCertificate.Find(other));
    }
}
