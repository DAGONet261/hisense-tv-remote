using HisenseTvRemote.Core.Vidaa;
using Microsoft.Data.Sqlite;

namespace HisenseTvRemote.Core.Storage;

/// <summary>TV enregistrées dans la base de l'app (table <c>tvs</c>).</summary>
public sealed class SqliteTvConfigStore(AppDatabase db) : ITvConfigStore
{
    public string Location => db.Path;

    public IReadOnlyList<TvEntry> Load()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, host, port, alias, name, mac, mac_wifi, brand, auth_mode, protocol_version, is_default
            FROM tvs ORDER BY rowid
            """;
        using var rd = cmd.ExecuteReader();
        var list = new List<TvEntry>();
        while (rd.Read())
        {
            string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
            list.Add(new TvEntry(rd.GetString(0), rd.GetString(1), S(3), S(4), S(5), rd.GetInt32(10) == 1)
            {
                Port = rd.GetInt32(2),
                MacWifi = S(6),
                Brand = rd.GetString(7),
                AuthMode = rd.GetString(8),
                ProtocolVersion = rd.IsDBNull(9) ? null : rd.GetInt32(9),
            });
        }
        return list;
    }

    public void AddTv(string ip, string? alias, string? name, string? mac, string? macWifi, string? brand, int? protocolVersion)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "DELETE FROM tvs WHERE id = $id OR (alias IS NOT NULL AND alias = $alias)", ("$id", ip), ("$alias", alias));
        Exec(c, tx, """
            INSERT INTO tvs(id, host, port, alias, name, mac, mac_wifi, brand, auth_mode, protocol_version, is_default)
            VALUES ($id, $id, 36669, $alias, $name, $mac, $wifi, $brand, 'auto', $pv,
                    NOT EXISTS (SELECT 1 FROM tvs WHERE is_default = 1))
            """,
            ("$id", ip), ("$alias", alias), ("$name", name), ("$mac", mac), ("$wifi", macWifi),
            ("$brand", brand ?? "his"), ("$pv", protocolVersion));
        tx.Commit();
    }

    public bool SetAlias(string aliasOrId, string newAlias)
    {
        if (TvLookup.Find(Load(), aliasOrId) is not { } tv) return false;
        using var c = db.Open();
        try { return Exec(c, null, "UPDATE tvs SET alias = $a WHERE id = $id", ("$a", newAlias), ("$id", tv.Id)) == 1; }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; } // alias déjà pris
    }

    public bool SetDefault(string aliasOrId)
    {
        if (TvLookup.Find(Load(), aliasOrId) is not { } tv) return false;
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "UPDATE tvs SET is_default = (id = $id)", ("$id", tv.Id));
        tx.Commit();
        return true;
    }

    public bool UpdateHost(string id, string newHost)
    {
        using var c = db.Open();
        return Exec(c, null, "UPDATE tvs SET host = $h WHERE id = $id", ("$h", newHost), ("$id", id)) == 1;
    }

    internal static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// Jetons d'appairage dans la base (table <c>tokens</c>), access/refresh token chiffrés DPAPI.
/// Un jeton indéchiffrable (base copiée sur un autre poste) est traité comme absent : réappairer.
/// </summary>
public sealed class SqliteTokenStore(AppDatabase db) : ITokenStore
{
    public string Location => db.Path;

    private const string Columns = """
        id, host, port, uuid, access_token, refresh_token, access_expires_at, refresh_expires_at,
        client_id, mqtt_username, auth_method, protocol_version
        """;

    public VidaaToken? Find(string host, int port, string? mac)
    {
        using var c = db.Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT {Columns} FROM tokens WHERE host = $h AND port = $p";
            cmd.Parameters.AddWithValue("$h", host);
            cmd.Parameters.AddWithValue("$p", port);
            using var rd = cmd.ExecuteReader();
            if (rd.Read()) return ToToken(rd);
        }
        if (string.IsNullOrEmpty(mac)) return null;

        // IP changée : même TV retrouvée par sa MAC.
        var wanted = ArpTable.NormalizeMac(mac);
        using var byMac = c.CreateCommand();
        byMac.CommandText = $"SELECT {Columns} FROM tokens WHERE uuid IS NOT NULL";
        using var r2 = byMac.ExecuteReader();
        while (r2.Read())
            if (ArpTable.NormalizeMac(r2.GetString(3)) == wanted) return ToToken(r2);
        return null;
    }

    private static VidaaToken? ToToken(SqliteDataReader rd)
    {
        string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
        byte[]? B(int i) => rd.IsDBNull(i) ? null : (byte[])rd.GetValue(i);
        var authMethod = S(10);
        var access = AppDatabase.Unprotect(B(4));
        var refresh = AppDatabase.Unprotect(B(5));
        // Chiffrés mais illisibles ici : autre poste ou autre compte Windows.
        if (authMethod != "static" && access is null && refresh is null) return null;
        return new VidaaToken($"{rd.GetString(1)}:{rd.GetInt32(2)}", access, refresh,
            access is null || rd.IsDBNull(6) ? null : rd.GetDouble(6),
            refresh is null || rd.IsDBNull(7) ? null : rd.GetDouble(7),
            rd.GetString(8), rd.GetString(9), S(3), rd.GetString(1), rd.GetInt32(2), authMethod,
            rd.IsDBNull(11) ? null : rd.GetInt32(11));
    }

    public void Save(string host, int port, string? accessToken, string? refreshToken, double accessDays, double refreshDays,
        string clientId, string username, string? uuid, string? authMethod, int? protocolVersion)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        // Même MAC sous une ancienne IP : une seule ligne par TV.
        if (!string.IsNullOrEmpty(uuid))
            foreach (var id in SameMacRows(c, tx, uuid))
                SqliteTvConfigStore.Exec(c, tx, "DELETE FROM tokens WHERE id = $id", ("$id", id));
        SqliteTvConfigStore.Exec(c, tx, """
            INSERT INTO tokens(host, port, uuid, access_token, refresh_token, access_expires_at, refresh_expires_at,
                               client_id, mqtt_username, auth_method, protocol_version, updated_at)
            VALUES ($h, $p, $u, $a, $r, $ae, $re, $cid, $user, $m, $pv, $now)
            ON CONFLICT(host, port) DO UPDATE SET
                uuid = excluded.uuid, access_token = excluded.access_token, refresh_token = excluded.refresh_token,
                access_expires_at = excluded.access_expires_at, refresh_expires_at = excluded.refresh_expires_at,
                client_id = excluded.client_id, mqtt_username = excluded.mqtt_username,
                auth_method = excluded.auth_method, protocol_version = excluded.protocol_version,
                updated_at = excluded.updated_at
            """,
            ("$h", host), ("$p", port), ("$u", uuid), ("$a", AppDatabase.Protect(accessToken)),
            ("$r", AppDatabase.Protect(refreshToken)),
            ("$ae", accessToken is null ? null : now + accessDays * 86400),
            ("$re", refreshToken is null ? null : now + refreshDays * 86400),
            ("$cid", clientId), ("$user", username), ("$m", authMethod), ("$pv", protocolVersion),
            ("$now", DateTime.UtcNow.ToString("O")));
        tx.Commit();
    }

    public bool Delete(string host, int port, string? mac)
    {
        if (Find(host, port, mac) is not { } t) return false;
        using var c = db.Open();
        return SqliteTvConfigStore.Exec(c, null, "DELETE FROM tokens WHERE host = $h AND port = $p",
            ("$h", t.Host), ("$p", t.Port)) > 0;
    }

    private static List<long> SameMacRows(SqliteConnection c, SqliteTransaction tx, string uuid)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, uuid FROM tokens WHERE uuid IS NOT NULL";
        using var rd = cmd.ExecuteReader();
        var wanted = ArpTable.NormalizeMac(uuid);
        var ids = new List<long>();
        while (rd.Read())
            if (ArpTable.NormalizeMac(rd.GetString(1)) == wanted) ids.Add(rd.GetInt64(0));
        return ids;
    }
}
