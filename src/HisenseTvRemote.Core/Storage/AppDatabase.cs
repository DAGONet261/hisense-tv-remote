using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HisenseTvRemote.Core.Storage;

/// <summary>
/// Base SQLite portable de l'application : TV, jetons d'appairage (chiffrés DPAPI), réglages et
/// historique du journal, dans un seul fichier. Voir contexte/base-sqlite.md.
/// </summary>
public sealed class AppDatabase
{
    public const string FileName = "HisenseTvRemote.db";
    private const int SchemaVersion = 1;
    /// <summary>Lignes de journal conservées (les plus anciennes sont supprimées au-delà).</summary>
    public const int JournalLimit = 2000;

    // Entropie DPAPI propre à l'app : un autre programme du même compte ne déchiffre pas par hasard.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HisenseTvRemote.tokens.v1");

    private readonly string _connectionString;

    public AppDatabase(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        Migrate();
    }

    public string Path { get; }

    /// <summary>
    /// Mode portable : à côté de l'exécutable si le dossier est inscriptible (clé USB, dossier copié…),
    /// sinon %APPDATA%\HisenseTvRemote (installation dans Program Files).
    /// </summary>
    public static string DefaultPath()
    {
        var portable = System.IO.Path.Combine(AppContext.BaseDirectory, FileName);
        if (File.Exists(portable) || IsWritable(AppContext.BaseDirectory)) return portable;
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HisenseTvRemote", FileName);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = System.IO.Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    internal SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(cmd.ExecuteScalar());
        if (version >= SchemaVersion) return;

        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS tvs (
                id               TEXT PRIMARY KEY,          -- identifiant (IP d'ajout, comme pyvidaa)
                host             TEXT NOT NULL,
                port             INTEGER NOT NULL DEFAULT 36669,
                alias            TEXT UNIQUE,
                name             TEXT,
                mac              TEXT,                      -- Ethernet (sert aux identifiants MQTT)
                mac_wifi         TEXT,
                brand            TEXT NOT NULL DEFAULT 'his',
                auth_mode        TEXT NOT NULL DEFAULT 'auto',
                protocol_version INTEGER,
                is_default       INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS tokens (
                id                 INTEGER PRIMARY KEY,
                host               TEXT NOT NULL,
                port               INTEGER NOT NULL,
                uuid               TEXT,                    -- MAC utilisée pour l'appairage
                access_token       BLOB,                    -- chiffré DPAPI (compte Windows courant)
                refresh_token      BLOB,                    -- chiffré DPAPI
                access_expires_at  REAL,
                refresh_expires_at REAL,
                client_id          TEXT NOT NULL,
                mqtt_username      TEXT NOT NULL,
                auth_method        TEXT,
                protocol_version   INTEGER,
                updated_at         TEXT NOT NULL,
                UNIQUE (host, port)
            );
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS journal (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at   TEXT NOT NULL,
                executable   TEXT NOT NULL,
                arguments    TEXT NOT NULL,                 -- JSON
                exit_code    INTEGER NOT NULL,
                stdout       TEXT NOT NULL,
                stderr       TEXT NOT NULL,
                duration_ms  INTEGER NOT NULL,
                timed_out    INTEGER NOT NULL,
                start_failed INTEGER NOT NULL
            );
            """ + $"PRAGMA user_version = {SchemaVersion};";
        cmd.ExecuteNonQuery();
    }

    // --------------------------------------------------------------- réglages ------------

    public string? GetSetting(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // --------------------------------------------------------------- journal -------------

    public void AddJournal(CommandResult r)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO journal(started_at, executable, arguments, exit_code, stdout, stderr, duration_ms, timed_out, start_failed)
            VALUES ($t, $e, $a, $x, $o, $err, $d, $to, $sf);
            DELETE FROM journal WHERE id <= (SELECT MAX(id) FROM journal) - $limit;
            """;
        cmd.Parameters.AddWithValue("$t", r.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$e", r.ExecutablePath);
        cmd.Parameters.AddWithValue("$a", JsonSerializer.Serialize(r.Arguments));
        cmd.Parameters.AddWithValue("$x", r.ExitCode);
        cmd.Parameters.AddWithValue("$o", r.StandardOutput);
        cmd.Parameters.AddWithValue("$err", r.StandardError);
        cmd.Parameters.AddWithValue("$d", (long)r.Duration.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$to", r.TimedOut ? 1 : 0);
        cmd.Parameters.AddWithValue("$sf", r.StartFailed ? 1 : 0);
        cmd.Parameters.AddWithValue("$limit", JournalLimit);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Les <paramref name="count"/> dernières commandes, de la plus ancienne à la plus récente.</summary>
    public IReadOnlyList<CommandResult> LoadJournal(int count)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT started_at, executable, arguments, exit_code, stdout, stderr, duration_ms, timed_out, start_failed
            FROM (SELECT * FROM journal ORDER BY id DESC LIMIT $n) ORDER BY id
            """;
        cmd.Parameters.AddWithValue("$n", count);
        using var rd = cmd.ExecuteReader();
        var list = new List<CommandResult>();
        while (rd.Read())
            list.Add(new CommandResult(rd.GetString(1), JsonSerializer.Deserialize<List<string>>(rd.GetString(2)) ?? [],
                rd.GetInt32(3), rd.GetString(4), rd.GetString(5), TimeSpan.FromMilliseconds(rd.GetInt64(6)),
                DateTime.Parse(rd.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
                rd.GetInt32(7) == 1, rd.GetInt32(8) == 1));
        return list;
    }

    public void ClearJournal()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM journal";
        cmd.ExecuteNonQuery();
    }

    // --------------------------------------------------------------- chiffrement ---------

    /// <summary>
    /// Chiffre avec DPAPI (compte Windows courant). Copiée sur un autre poste ou sous un autre compte,
    /// la base reste lisible mais les jetons non : il suffit de refaire l'appairage.
    /// </summary>
    internal static byte[]? Protect(string? secret)
    {
        if (secret is null || !OperatingSystem.IsWindows()) return null;
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
    }

    /// <summary>null si absent ou indéchiffrable (autre poste/compte).</summary>
    internal static string? Unprotect(byte[]? data)
    {
        if (data is null || !OperatingSystem.IsWindows()) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
    }
}
