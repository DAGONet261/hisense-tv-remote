using System.Text.Json;
using HisenseTvRemote.Core.Storage;

namespace HisenseTvRemote.Core;

/// <summary>Réglages de l'app, conservés dans la base SQLite (clé <see cref="DbKey"/>).</summary>
public sealed class Settings
{
    public const string DbKey = "app.settings";
    private AppDatabase? _db;

    /// <summary>TV pilotée au dernier lancement.</summary>
    public string? CurrentAlias { get; set; }

    /// <summary>Dossier du certificat client VIDAA (null = <see cref="Vidaa.ClientCertificate.DefaultDirectory"/>).</summary>
    public string? CertDirectory { get; set; }

    /// <summary>Mode souris : distance (pixels) pour un pas de navigation. Plus petit = plus sensible.</summary>
    public int MouseStepPixels { get; set; } = 90;

    public static Settings Load(AppDatabase db)
    {
        Settings s;
        try { s = db.GetSetting(DbKey) is { } json ? JsonSerializer.Deserialize<Settings>(json) ?? new() : new(); }
        catch (JsonException) { s = new Settings(); }
        s._db = db;
        return s;
    }

    public void Save() =>
        _db?.SetSetting(DbKey, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
