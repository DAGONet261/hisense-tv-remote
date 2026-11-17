using System.Diagnostics;
using System.Text.Json.Nodes;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.Vidaa;

namespace HisenseTvRemote.Core;

/// <summary>
/// Ce qui exécute réellement les commandes : le moteur natif (<see cref="NativeTv"/>) en production,
/// un faux moteur en mémoire dans les tests.
/// </summary>
public interface ITvEngine : IDisposable
{
    /// <summary>Exécute une commande au format « tv » (ex. <c>--tv salon key home</c>) ; null si inconnue.</summary>
    Task<CommandResult?> TryRunAsync(IReadOnlyList<string> args, CancellationToken ct = default);

    IPairingSession StartPairing(string? alias);

    /// <summary>Dernière liste d'apps reçue pour cette TV (champs bruts : name, appId, httpIcon…).</summary>
    IReadOnlyList<JsonObject>? AppsOf(string? alias);

    /// <summary>Dernière liste de sources reçue pour cette TV (sourceid, sourcename, displayname…).</summary>
    IReadOnlyList<JsonObject>? SourcesOf(string? alias);

    /// <summary>Résultat de la dernière recherche réseau.</summary>
    IReadOnlyList<DiscoveredTv> LastDiscovered { get; }
}

/// <summary>
/// Point d'entrée unique des commandes TV pour l'interface. Ne bloque jamais le thread appelant ;
/// chaque appel produit un <see cref="CommandResult"/> et déclenche <see cref="CommandCompleted"/>
/// (journal). Les commandes sont écrites comme en ligne de commande (<c>--tv salon key home</c>), ce qui
/// rend le journal lisible.
/// </summary>
public sealed class TvController : IDisposable
{
    private readonly ITvEngine _engine;
    private readonly ITvConfigStore _tvStore;

    public TvController(ITvEngine engine, ITvConfigStore tvStore)
    {
        _engine = engine;
        _tvStore = tvStore;
    }

    /// <summary>Contrôleur de production : moteur natif sur la base de l'app.</summary>
    public static TvController ForDatabase(AppDatabase db, Settings settings)
    {
        var tvs = new SqliteTvConfigStore(db);
        var engine = new NativeTv(() => tvs, () => new SqliteTokenStore(db), () => settings.CertDirectory);
        return new TvController(engine, tvs);
    }

    public ITvEngine Engine => _engine;

    /// <summary>Là où sont rangées les TV (base SQLite).</summary>
    public ITvConfigStore TvStore => _tvStore;

    /// <summary>Déclenché après chaque commande (peut venir d'un thread du pool).</summary>
    public event Action<CommandResult>? CommandCompleted;

    public async Task<CommandResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var list = args.ToList();
        var started = DateTime.Now;
        var sw = Stopwatch.StartNew();
        var r = await _engine.TryRunAsync(list, ct).ConfigureAwait(false)
                ?? new CommandResult(NativeTv.Label, list, 1, "", $"Commande non prise en charge : {string.Join(' ', list)}",
                    sw.Elapsed, started);
        CommandCompleted?.Invoke(r);
        return r;
    }

    /// <summary>Commande ciblant une TV : <c>--tv alias …</c> (TV par défaut si alias vide).</summary>
    public Task<CommandResult> RunForTvAsync(string? alias, params string[] args)
    {
        var all = new List<string>();
        if (!string.IsNullOrWhiteSpace(alias)) { all.Add("--tv"); all.Add(alias); }
        all.AddRange(args);
        return RunAsync(all);
    }

    // --- Télécommande / apps / sources / alimentation -------------------------------------
    public Task<CommandResult> KeyAsync(string? alias, string key) => RunForTvAsync(alias, "key", key);
    public Task<CommandResult> VolumeAsync(string? alias, bool up) => RunForTvAsync(alias, "volume", up ? "up" : "down");
    public Task<CommandResult> VolumeGetAsync(string? alias) => RunForTvAsync(alias, "volume", "get");
    public Task<CommandResult> AppAsync(string? alias, string app) => RunForTvAsync(alias, "app", app);
    public Task<CommandResult> AppListAsync(string? alias) => RunForTvAsync(alias, "app", "list");
    /// <summary>Nom court (hdmi1, hdmi2, tv, av) ou identifiant renvoyé par <c>source list</c> (HDMI3, AVS…).</summary>
    public Task<CommandResult> SourceAsync(string? alias, string source) => RunForTvAsync(alias, "source", source);
    public Task<CommandResult> SourceListAsync(string? alias) => RunForTvAsync(alias, "source", "list");
    public Task<CommandResult> StatusAsync(string? alias) => RunForTvAsync(alias, "status");
    public Task<CommandResult> WakeAsync(string? alias) => RunForTvAsync(alias, "wake");
    /// <summary>Texte pour le champ de saisie actif (recherche…) : replace, insert ou clear.</summary>
    public Task<CommandResult> TextAsync(string? alias, string text, string action = "replace") =>
        action == "clear" ? RunForTvAsync(alias, "text", "clear") : RunForTvAsync(alias, "text", action, text);
    public Task<CommandResult> OnAsync(string? alias) => RunForTvAsync(alias, "on");
    public Task<CommandResult> OffAsync(string? alias) => RunForTvAsync(alias, "off");

    // --- Multi-TV --------------------------------------------------------------------------
    public Task<CommandResult> ConfigAddAsync(string ip, string alias) => RunAsync(["config", "add", ip, "--alias", alias]);
    public Task<CommandResult> ConfigSetDefaultAsync(string aliasOrId) => RunAsync(["config", "set-default", aliasOrId]);
    public Task<CommandResult> ConfigSetAliasAsync(string aliasOrId, string newAlias) =>
        RunAsync(["--tv", aliasOrId, "config", "set-alias", newAlias]);
    public Task<CommandResult> DiscoverAsync() => RunAsync(["discover"]);

    // --- Authentification ------------------------------------------------------------------
    public Task<CommandResult> AuthStatusAsync(string? alias) => RunForTvAsync(alias, "auth", "status");
    public Task<CommandResult> AuthRefreshAsync(string? alias) => RunForTvAsync(alias, "auth", "refresh");
    public Task<CommandResult> AuthClearAsync(string? alias) => RunForTvAsync(alias, "auth", "clear");

    /// <summary>Démarre l'appairage par code PIN ; le résultat final passe aussi au journal.</summary>
    public IPairingSession StartPairing(string? alias)
    {
        var session = _engine.StartPairing(string.IsNullOrWhiteSpace(alias) ? null : alias);
        session.Completed += r => CommandCompleted?.Invoke(r);
        return session;
    }

    public void Dispose() => _engine.Dispose();
}
