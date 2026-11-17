using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.ViewModels;
using HisenseTvRemote.Core.Vidaa;
using Microsoft.Data.Sqlite;

namespace HisenseTvRemote.Tests;

/// <summary>Raccourcis clavier et identifiants de source.</summary>
public class ShortcutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sc-" + Guid.NewGuid().ToString("N"));

    public ShortcutTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string? TvKeyOf(string key, KeyMods mods = KeyMods.None, char? ch = null) =>
        KeyboardShortcuts.Match(key, ch, mods)?.TvKey;

    [Fact]
    public void Navigation_entree_echap()
    {
        Assert.Equal("up", TvKeyOf("Up"));
        Assert.Equal("left", TvKeyOf("Left"));
        Assert.Equal("ok", TvKeyOf("Return"));
        Assert.Equal("ok", TvKeyOf("Enter"));
        Assert.Equal("returns", TvKeyOf("Escape"));
        Assert.Equal("returns", TvKeyOf("Back"));
        Assert.Equal("home", TvKeyOf("Home"));
        Assert.Equal("channelup", TvKeyOf("Prior"));
        Assert.Null(KeyboardShortcuts.Match("Up", null, KeyMods.Shift));        // Maj + ↑ : rien
    }

    [Fact]
    public void Volume_muet_lecture()
    {
        Assert.Equal(ShortcutCommand.VolumeUp, KeyboardShortcuts.Match("Up", null, KeyMods.Ctrl)!.Command);
        Assert.Equal(ShortcutCommand.VolumeDown, KeyboardShortcuts.Match("Subtract", '-', KeyMods.None)!.Command);
        Assert.Equal(ShortcutCommand.Mute, KeyboardShortcuts.Match("M", 'm', KeyMods.Ctrl)!.Command);
        Assert.Equal(ShortcutCommand.PlayPause, KeyboardShortcuts.Match("Space", ' ', KeyMods.None)!.Command);
        Assert.Equal("backs", KeyboardShortcuts.Match("MediaPreviousTrack", null, KeyMods.None)!.TvKey);   // rembobiner
        Assert.Equal("forwards", KeyboardShortcuts.Match("MediaNextTrack", null, KeyMods.None)!.TvKey);
        Assert.Null(KeyboardShortcuts.Match("M", 'm', KeyMods.None));            // lettre seule : libre
        Assert.Null(KeyboardShortcuts.Match("D5", '(', KeyMods.None));           // chiffre seul : libre
    }

    [Fact]
    public void Zoom_par_caractere_qwerty_et_azerty()
    {
        // AZERTY : « - » est sur la touche 6, « + » sur la touche « = » avec Maj.
        Assert.Equal("zoomout", TvKeyOf("D6", KeyMods.Ctrl, '-'));
        Assert.Equal("zoomin", TvKeyOf("OemPlus", KeyMods.Ctrl, '='));
        Assert.Equal("zoomin", TvKeyOf("OemPlus", KeyMods.Ctrl | KeyMods.Shift, '='));
        Assert.Equal("zoomin", TvKeyOf("Add", KeyMods.Ctrl, '+'));
        Assert.Equal("zoomout", TvKeyOf("OemMinus", KeyMods.Ctrl, '-'));        // QWERTY
        Assert.Null(TvKeyOf("D6", KeyMods.None, '-'));                          // sans Ctrl : rien
    }

    [Fact]
    public void Pave_numerique_chiffres_TV_mais_rangee_du_haut_libre()
    {
        Assert.Equal("7", TvKeyOf("NumPad7"));
        Assert.Equal("0", TvKeyOf("NumPad0"));
        Assert.Equal("red", TvKeyOf("NumPad1", KeyMods.Ctrl));                   // Ctrl + 1 reste une couleur
        Assert.Null(KeyboardShortcuts.Match("D7", 'è', KeyMods.None));           // rangée du haut : saisie de texte
        Assert.Null(KeyboardShortcuts.Match("A", 'a', KeyMods.None));
    }

    [Fact]
    public void Couleurs_et_application()
    {
        Assert.Equal("red", TvKeyOf("D1", KeyMods.Ctrl, '&'));
        Assert.Equal("blue", TvKeyOf("NumPad4", KeyMods.Ctrl));
        Assert.Equal(ShortcutCommand.ShowHelp, KeyboardShortcuts.Match("F1", null, KeyMods.None)!.Command);
        Assert.Equal(ShortcutCommand.OpenJournal, KeyboardShortcuts.Match("J", 'j', KeyMods.Ctrl)!.Command);
    }

    [Fact]
    public void Aucun_doublon_et_aide_complete()
    {
        var keys = KeyboardShortcuts.All.SelectMany(s => s.Keys.Select(k => (k, s.Mods))).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(KeyboardShortcuts.All.Where(s => s.Command == ShortcutCommand.TvKey),
            s => Assert.NotNull(VidaaCatalog.ResolveKey(s.TvKey!)));
        Assert.Contains(KeyboardShortcuts.ForHelp(), g => g.Key == "Navigation");
        Assert.DoesNotContain(KeyboardShortcuts.All, s => s.TvKey == "power");      // jamais par accident
    }

    [Fact]
    public async Task Raccourcis_executes_par_le_ViewModel()
    {
        var db = new AppDatabase(Path.Combine(_dir, AppDatabase.FileName));
        var tvs = new SqliteTvConfigStore(db);
        tvs.AddTv("192.168.88.4", "salon", null, null, null, null, null);
        var engine = new FakeEngine();
        var vm = new RemoteViewModel(new TvController(engine, tvs), null, "salon");
        vm.ReloadTvs();

        Assert.True(await vm.RunShortcutAsync(KeyboardShortcuts.Match("Return", null, KeyMods.None)!));
        Assert.Equal(["--tv", "salon", "key", "ok"], engine.Received[^1]);

        var space = KeyboardShortcuts.Match("Space", ' ', KeyMods.None)!;
        await vm.RunShortcutAsync(space);
        Assert.Equal("pause", engine.Received[^1][^1]);
        await vm.RunShortcutAsync(space);
        Assert.Equal("play", engine.Received[^1][^1]);

        await vm.RunShortcutAsync(KeyboardShortcuts.Match("Up", null, KeyMods.Ctrl)!);
        Assert.Equal(["--tv", "salon", "volume", "up"], engine.Received[^1]);
        Assert.False(await vm.RunShortcutAsync(KeyboardShortcuts.Match("F1", null, KeyMods.None)!)); // géré par la vue
    }

    [Fact]
    public void Mode_souris_mouvements_traduits_en_navigation()
    {
        var p = new PointerToKeys(threshold: 100);
        Assert.Empty(p.Add(40, 10));                                   // petit mouvement : rien
        Assert.Equal(["right"], p.Add(70, 0));                          // 110 px cumulés → un pas
        Assert.Equal(["down", "down"], p.Add(5, 230));                  // axe dominant, deux pas
        Assert.Equal(["left"], p.Add(-120, 30));                        // l'autre axe repart de zéro
        Assert.Empty(p.Add(0, 60));
        p.Reset();
        Assert.Empty(p.Add(0, 60));                                     // remis à zéro après un clic
        Assert.Equal(ShortcutCommand.OpenMouseMode, KeyboardShortcuts.Match("F11", null, KeyMods.None)!.Command);
    }

    [Fact]
    public void Mode_souris_sensibilite_reglable_et_bornee()
    {
        var p = new PointerToKeys(threshold: 90);
        Assert.Equal(100, p.Adjust(moreSensitive: false));
        Assert.Equal(90, p.Adjust(moreSensitive: true));
        for (var i = 0; i < 50; i++) p.Adjust(moreSensitive: true);
        Assert.Equal(PointerToKeys.MinThreshold, (int)p.Threshold);
        for (var i = 0; i < 50; i++) p.Adjust(moreSensitive: false);
        Assert.Equal(PointerToKeys.MaxThreshold, (int)p.Threshold);
    }

    [Fact]
    public void Sources_identifiants_textuels_de_la_TV()
    {
        // La 55A53FXVT ignore les numéros de pyvidaa (hdmi1 → « 3 ») : seuls AV et Partage de musique
        // marchaient, justement parce qu'ils n'étaient pas convertis.
        Assert.Equal("HDMI1", VidaaCatalog.SourceIds["hdmi1"]);
        Assert.Equal("HDMI1", VidaaCatalog.SourceIds["HDMI1"]);
        Assert.Equal("TV", VidaaCatalog.SourceIds["tv"]);
        Assert.Equal("AVS", VidaaCatalog.SourceIds["av"]);
        Assert.False(VidaaCatalog.SourceIds.ContainsKey("MusicSharing"));          // envoyé tel quel
    }
}
