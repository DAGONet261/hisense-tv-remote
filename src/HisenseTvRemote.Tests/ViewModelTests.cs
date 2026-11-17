using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.ViewModels;
using Microsoft.Data.Sqlite;

namespace HisenseTvRemote.Tests;

/// <summary>ViewModel de l'écran principal, sur le faux moteur (sans TV ni réseau).</summary>
public class ViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vm-" + Guid.NewGuid().ToString("N"));
    private readonly AppDatabase _db;
    private readonly FakeEngine _engine = new();

    public ViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new AppDatabase(Path.Combine(_dir, AppDatabase.FileName));
        var tvs = new SqliteTvConfigStore(_db);
        tvs.AddTv("192.168.88.4", "salon", "TV Salon", "e4:3b:c9:f9:5c:7e", null, "his", 3290);
        tvs.AddTv("192.168.88.30", "chambre", null, null, null, null, null);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private (RemoteViewModel Vm, List<string?> Saved) Make(string? initial = null, IconCache? icons = null)
    {
        var tv = new TvController(_engine, new SqliteTvConfigStore(_db));
        var saved = new List<string?>();
        return (new RemoteViewModel(tv, icons, initial, saved.Add), saved);
    }

    [Fact]
    public void ReloadTvs_choisit_la_TV_par_defaut_et_la_memorise()
    {
        var (vm, saved) = Make(initial: "inconnue");
        vm.ReloadTvs();
        Assert.Equal(["salon", "chambre"], vm.Tvs);
        Assert.Equal("salon", vm.CurrentTv);
        Assert.Equal("salon", saved[^1]);
    }

    [Fact]
    public async Task Touche_envoyee_a_la_TV_courante_et_statut_mis_a_jour()
    {
        var (vm, _) = Make("chambre");
        vm.ReloadTvs();
        await vm.KeyCommand.ExecuteAsync("home");
        Assert.Equal(["--tv", "chambre", "key", "home"], _engine.Received[^1]);
        Assert.Equal("Sent: KEY_HOME", vm.Status);
        Assert.False(vm.LastFailed);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Echec_signale_en_rouge_avec_conseil()
    {
        var (vm, _) = Make("salon");
        vm.ReloadTvs();
        var r = await vm.Run(() => new TvController(_engine, new SqliteTvConfigStore(_db)).StatusAsync("salon"));
        Assert.False(r.Success);
        Assert.True(vm.LastFailed);
        Assert.Contains("Code 1", vm.Status);
        Assert.Contains("Retrouver l'IP", vm.Status);
    }

    [Fact]
    public async Task Liste_d_apps_avec_logos_en_cache()
    {
        var icons = new IconCache(Path.Combine(_dir, "icons"));
        // Logo déjà en cache : pas d'accès réseau pendant le test.
        var url = IconCache.ExtractUrl(_engine.InstalledApps[0]["httpIcon"]!.ToString())!;
        Directory.CreateDirectory(icons.Folder);
        File.WriteAllBytes(icons.PathFor(url), [0x89, (byte)'P', (byte)'N', (byte)'G']);

        var (vm, _) = Make("salon", icons);
        Assert.Equal(KeyCatalog.Apps, vm.Apps.Select(a => a.Name));          // raccourcis avant la première liste
        await vm.RefreshAppsCommand.ExecuteAsync(null);
        Assert.Equal(["Netflix", "VIDAA Art"], vm.Apps.Select(a => a.Name));
        Assert.Equal(icons.PathFor(url), vm.Apps[0].IconPath);
        Assert.Null(vm.Apps[1].IconPath);                                       // pas d'icône : initiale affichée
        Assert.Equal("V", vm.Apps[1].Initial);
        Assert.Equal("2 applications sur « salon ».", vm.Status);
    }

    [Fact]
    public async Task Volume_relu_apres_changement()
    {
        var (vm, _) = Make("salon");
        vm.ReloadTvs();
        await vm.ChangeVolumeCommand.ExecuteAsync("up");
        await Task.Delay(1200);                                                 // relecture différée (rafale)
        Assert.Equal(12, vm.Volume);
        Assert.Contains(_engine.Received, a => a.SequenceEqual(new[] { "--tv", "salon", "volume", "get" }));
    }

    [Fact]
    public async Task Molette_un_cran_un_pas_et_fractions_cumulees()
    {
        var (vm, _) = Make("salon");
        vm.ReloadTvs();
        int Count(string dir) => _engine.Received.Count(a => a.SequenceEqual(new[] { "--tv", "salon", "volume", dir }));

        await vm.WheelVolumeAsync(2 * RemoteViewModel.WheelNotch);       // deux crans vers le haut
        Assert.Equal(2, Count("up"));
        await vm.WheelVolumeAsync(-RemoteViewModel.WheelNotch);          // un cran vers le bas
        Assert.Equal(1, Count("down"));

        await vm.WheelVolumeAsync(60);                                   // pavé tactile : demi-cran…
        Assert.Equal(2, Count("up"));
        await vm.WheelVolumeAsync(60);                                   // …puis l'autre moitié
        Assert.Equal(3, Count("up"));
    }

    [Fact]
    public async Task Volume_affiche_tout_de_suite_puis_confirme()
    {
        var (vm, _) = Make("salon");
        vm.ReloadTvs();
        vm.Volume = 7;
        await vm.WheelVolumeAsync(RemoteViewModel.WheelNotch);
        Assert.Equal(8, vm.Volume);                                      // immédiat
        await Task.Delay(1200);
        Assert.Equal(12, vm.Volume);                                     // valeur relue sur la TV (faux moteur)
    }

    [Fact]
    public async Task Saisie_de_texte_instantanee_a_chaque_lettre()
    {
        var (vm, _) = Make("salon");
        vm.ReloadTvs();
        foreach (var t in new[] { "l", "lo", "lof", "lofi" }) vm.TypedText = t;
        await Task.Delay(100);
        var texts = _engine.Received.Where(a => a.Contains("text")).Select(a => a[^1]).ToList();
        Assert.Equal(["l", "lo", "lof", "lofi"], texts);                       // une par lettre, dans l'ordre

        vm.ClearTextCommand.Execute(null);
        await Task.Delay(100);
        Assert.Equal(["--tv", "salon", "text", "clear"], _engine.Received[^1]);

        vm.TypedText = "été 2026 !";                                           // accents et symboles tels quels
        await Task.Delay(100);
        Assert.Equal(["--tv", "salon", "text", "replace", "été 2026 !"], _engine.Received[^1]);
        await vm.FlushTypedTextAsync();                                         // Entrée : déjà envoyé → rien
        Assert.Single(_engine.Received.Where(a => a.Contains("été 2026 !")));
    }

    [Fact]
    public void Libelles_et_touches_secondaires()
    {
        var (vm, _) = Make();
        Assert.Equal("TV Channels", RemoteViewModel.CleanLabel("TV Channels▶"));
        Assert.Equal(10, vm.DigitKeys.Count);
        Assert.Equal("0", vm.DigitKeys[^1].Key);
        Assert.All(vm.ExtraKeys, k => Assert.NotEqual(k.Key, k.Label));          // toutes ont un nom français
    }

    [Fact]
    public void Cache_des_logos_formats_et_urls()
    {
        const string raw = "data:image/png;base64,iconDownloadhttps://img.vidaahub.com/vidaa/2024/12/202412101945116636.jpg";
        Assert.Equal("https://img.vidaahub.com/vidaa/2024/12/202412101945116636.jpg", IconCache.ExtractUrl(raw));
        Assert.Null(IconCache.ExtractUrl(""));
        Assert.Null(IconCache.ExtractUrl("data:image/png;base64,AAAA"));
        var cache = new IconCache(_dir);
        Assert.EndsWith(".jpg", cache.PathFor(IconCache.ExtractUrl(raw)!));     // JPEG malgré le préfixe « png »
        Assert.True(IconCache.IsImage([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(IconCache.IsImage("<html>"u8.ToArray()));
    }
}
