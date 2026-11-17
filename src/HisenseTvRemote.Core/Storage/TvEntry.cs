namespace HisenseTvRemote.Core.Storage;

/// <summary>Une TV enregistrée dans la base de l'app.</summary>
public sealed record TvEntry(string Id, string? Host, string? Alias, string? Name, string? Mac, bool IsDefault)
{
    public int Port { get; init; } = 36669;
    public string Brand { get; init; } = "his";
    /// <summary>auto | dynamic | static (choix du schéma d'identifiants MQTT).</summary>
    public string AuthMode { get; init; } = "auto";
    public int? ProtocolVersion { get; init; }
    /// <summary>MAC de la carte Wi-Fi (la TV en a deux ; <see cref="Mac"/> est l'Ethernet).</summary>
    public string? MacWifi { get; init; }

    /// <summary>Ce qu'on passe à <c>--tv</c> : l'alias s'il existe, sinon l'identifiant.</summary>
    public string Key => string.IsNullOrWhiteSpace(Alias) ? Id : Alias;

    public override string ToString() =>
        $"{Key}{(IsDefault ? " (défaut)" : "")} — {Host ?? Id}{(Name is null ? "" : " — " + Name)}" +
        (Mac is null ? "" : $" — MAC {Mac}");
}

/// <summary>Recherche d'une TV par alias ou identifiant.</summary>
public static class TvLookup
{
    /// <summary>Par alias, sinon par identifiant (comparaison exacte).</summary>
    public static TvEntry? Find(IEnumerable<TvEntry> tvs, string? aliasOrId) =>
        aliasOrId is null ? null : tvs.FirstOrDefault(t => t.Alias == aliasOrId) ?? tvs.FirstOrDefault(t => t.Id == aliasOrId);

    /// <summary>TV désignée par <c>--tv</c>, ou la TV par défaut si <paramref name="aliasOrId"/> est vide.</summary>
    public static TvEntry? Resolve(IReadOnlyList<TvEntry> tvs, string? aliasOrId) =>
        string.IsNullOrWhiteSpace(aliasOrId) ? tvs.FirstOrDefault(t => t.IsDefault) : Find(tvs, aliasOrId);
}
