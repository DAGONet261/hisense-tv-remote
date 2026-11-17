using HisenseTvRemote.Core.Vidaa;

namespace HisenseTvRemote.Core.Storage;

/// <summary>Liste des TV et leur configuration (implémentation : <see cref="SqliteTvConfigStore"/>).</summary>
public interface ITvConfigStore
{
    /// <summary>Où sont rangées les TV (affiché à l'utilisateur).</summary>
    string Location { get; }

    IReadOnlyList<TvEntry> Load();
    void AddTv(string ip, string? alias, string? name, string? mac, string? macWifi, string? brand, int? protocolVersion);
    bool SetAlias(string aliasOrId, string newAlias);
    bool SetDefault(string aliasOrId);
    bool UpdateHost(string id, string newHost);
}

/// <summary>Jetons d'appairage (implémentation : <see cref="SqliteTokenStore"/>, chiffrée DPAPI).</summary>
public interface ITokenStore
{
    string Location { get; }

    /// <summary>Par host:port, sinon par MAC (survit à un changement d'IP).</summary>
    VidaaToken? Find(string host, int port, string? mac);

    void Save(string host, int port, string? accessToken, string? refreshToken, double accessDays, double refreshDays,
        string clientId, string username, string? uuid, string? authMethod, int? protocolVersion);

    bool Delete(string host, int port, string? mac);
}
