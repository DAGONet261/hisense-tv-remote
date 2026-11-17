using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.Vidaa;
using Microsoft.Data.Sqlite;

namespace HisenseTvRemote.Tests;

/// <summary>Base SQLite portable : TV, jetons chiffrés, réglages, journal.</summary>
public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "db-" + Guid.NewGuid().ToString("N"));
    private readonly AppDatabase _db;

    public StorageTests()
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
    public void Tv_ajout_alias_defaut_host()
    {
        var store = new SqliteTvConfigStore(_db);
        store.AddTv("192.168.88.4", "salon", "TV Salon", "e4:3b:c9:f9:5c:7e", "38:64:07:35:96:52", "his", 3290);
        store.AddTv("192.168.88.30", "chambre", null, null, null, null, null);

        var tvs = store.Load();
        Assert.Equal(["salon", "chambre"], tvs.Select(t => t.Key));
        Assert.True(tvs[0].IsDefault);
        Assert.Equal("38:64:07:35:96:52", tvs[0].MacWifi);
        Assert.Equal(3290, tvs[0].ProtocolVersion);

        Assert.True(store.SetAlias("salon", "séjour"));
        Assert.False(store.SetAlias("chambre", "séjour"));       // alias déjà pris
        Assert.True(store.SetDefault("chambre"));
        Assert.True(store.UpdateHost("192.168.88.4", "192.168.88.19"));
        tvs = store.Load();
        Assert.Equal("chambre", tvs.Single(t => t.IsDefault).Key);
        Assert.Equal("192.168.88.19", TvLookup.Find(tvs, "séjour")!.Host);
        Assert.False(store.SetDefault("inconnue"));
    }

    [Fact]
    public void Jetons_chiffres_au_repos_et_retrouves_par_mac()
    {
        var store = new SqliteTokenStore(_db);
        store.Save("192.168.88.4", 36669, "ACCESS-SECRET-123", "REFRESH-SECRET-456", 2, 30, "cid", "user",
            "e4:3b:c9:f9:5c:7e", "modern", 3290);

        var t = store.Find("192.168.88.19", 36669, "E4:3B:C9:F9:5C:7E")!;    // IP changée : trouvé par MAC
        Assert.Equal(("ACCESS-SECRET-123", "REFRESH-SECRET-456"), (t.AccessToken, t.RefreshToken));
        Assert.True(t.AccessValid);
        Assert.InRange(t.RefreshExpiresIn, 29 * 86400, 30 * 86400);

        SqliteConnection.ClearAllPools();
        var raw = File.ReadAllBytes(_db.Path).Concat(ReadIfExists(_db.Path + "-wal")).ToArray();
        var text = System.Text.Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("ACCESS-SECRET-123", text);
        Assert.DoesNotContain("REFRESH-SECRET-456", text);

        // Réenregistré sous la nouvelle IP : une seule ligne pour cette TV.
        store.Save("192.168.88.19", 36669, "A2", "R2", 2, 30, "cid", "user", "e4:3b:c9:f9:5c:7e", "modern", 3290);
        Assert.Null(store.Find("192.168.88.4", 36669, null));
        Assert.True(store.Delete("192.168.88.19", 36669, null));
        Assert.Null(store.Find("192.168.88.19", 36669, "e4:3b:c9:f9:5c:7e"));
    }

    [Fact]
    public void Jeton_indechiffrable_equivaut_a_pas_de_jeton()
    {
        // Simule une base copiée sur un autre poste : blobs présents mais illisibles par DPAPI ici.
        new SqliteTokenStore(_db).Save("192.168.88.4", 36669, "a", "r", 2, 30, "cid", "user", null, "modern", 3290);
        using (var c = new SqliteConnection($"Data Source={_db.Path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE tokens SET access_token = x'0102', refresh_token = x'0304'";
            cmd.ExecuteNonQuery();
        }
        Assert.Null(new SqliteTokenStore(_db).Find("192.168.88.4", 36669, null));
    }

    [Fact]
    public void Reglages_et_journal()
    {
        var s = Settings.Load(_db);
        s.CurrentAlias = "chambre";
        s.CertDirectory = @"D:\certs";
        s.Save();                                             // sans chemin : va en base
        var back = Settings.Load(_db);
        Assert.Equal(("chambre", @"D:\certs"), (back.CurrentAlias, back.CertDirectory));

        for (var i = 0; i < 3; i++)
            _db.AddJournal(new CommandResult(NativeTv.Label, ["--tv", "salon", "key", $"k{i}"], i, "out", "err",
                TimeSpan.FromMilliseconds(5), new DateTime(2026, 11, 17, 12, 0, i)));
        var j = _db.LoadJournal(2);
        Assert.Equal(["tv --tv salon key k1", "tv --tv salon key k2"], j.Select(r => r.CommandLine));
        Assert.Equal(new DateTime(2026, 11, 17, 12, 0, 2), j[1].StartedAt);
        _db.ClearJournal();
        Assert.Empty(_db.LoadJournal(10));
    }

    private static byte[] ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : [];
}
