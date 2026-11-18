using System.Net;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>Ce que l'écran « Partager vers la TV » demande au lecteur (remplacé par un faux dans les tests).</summary>
public interface IMediaPlayer : IAsyncDisposable
{
    /// <summary>Envoie le fichier à la TV et lance l'affichage / la lecture (durée annoncée si connue).</summary>
    Task ShowAsync(string path, MediaType type, string title, TimeSpan? duration = null, CancellationToken ct = default);
    Task PlayAsync(CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);
    Task<PlaybackInfo> GetInfoAsync(CancellationToken ct = default);

    /// <summary>La TV a téléchargé (une partie d') un fichier servi. Peut venir d'un autre thread.</summary>
    event Action<string>? RequestServed;

    /// <summary>
    /// Jusqu'où la TV a déjà chargé le fichier en cours, à partir de <paramref name="position"/>
    /// (partie claire de la barre de progression) ; null si inconnu.
    /// </summary>
    TimeSpan? BufferedUntil(TimeSpan position, TimeSpan? duration);
}

/// <summary>
/// Partage DLNA vers une TV : serveur HTTP local (le PC) + lecteur DLNA de la TV. Démarré au premier envoi
/// (découverte du lecteur, ouverture du port) ; arrêté par <see cref="DisposeAsync"/>.
/// </summary>
public sealed class DlnaPlayer(IPAddress tv, HttpClient http) : IMediaPlayer
{
    /// <summary>Fichiers encore servis : la TV peut relire l'avant-dernier quelques instants après un changement.</summary>
    private const int KeepPublished = 3;

    private readonly Queue<string> _published = new();
    private MediaContent? _current;
    private DlnaRenderer? _renderer;
    private MediaHttpServer? _server;

    public event Action<string>? RequestServed;

    public string? TvName => _renderer?.Name;

    private async Task<DlnaRenderer> RendererAsync(CancellationToken ct)
    {
        _renderer ??= await DlnaRenderer.FindAsync(tv, http, ct).ConfigureAwait(false);
        if (_server is null)
        {
            _server = new MediaHttpServer(MediaHttpServer.LocalAddressFor(tv), [tv]);
            _server.RequestServed += m => RequestServed?.Invoke(m);
        }
        return _renderer;
    }

    public async Task ShowAsync(string path, MediaType type, string title, TimeSpan? duration = null, CancellationToken ct = default)
    {
        var renderer = await RendererAsync(ct).ConfigureAwait(false);
        var content = await ContentForAsync(path, type).ConfigureAwait(false);
        var url = _server!.Publish(content, type);
        _current = content;
        _published.Enqueue(url);
        while (_published.Count > KeepPublished) _server.Unpublish(_published.Dequeue());
        try { await renderer.SetUriAsync(url, title, type, duration, ct).ConfigureAwait(false); }
        catch (DlnaException)
        {
            // Certains lecteurs refusent de changer de fichier en cours de lecture (erreur 701) : arrêt puis reprise.
            await renderer.StopAsync(ct).ConfigureAwait(false);
            await renderer.SetUriAsync(url, title, type, duration, ct).ConfigureAwait(false);
        }
        await renderer.PlayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// MP4/MOV : servis réorganisés (image et son alternés finement) pour une lecture fluide par la TV ;
    /// le reste tel quel. Vue gardée en cache (index recalculé seulement si le fichier change).
    /// </summary>
    private async Task<MediaContent> ContentForAsync(string path, MediaType type)
    {
        if (type.Kind != MediaKind.Video || !Mp4Extensions.Contains(Path.GetExtension(path))) return new FileContent(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_mp4Cache is { } c && c.Path == path && c.Stamp == stamp) return c.Content;
        var content = await Task.Run(() => (MediaContent?)Mp4Interleaver.TryCreate(path)).ConfigureAwait(false) ?? new FileContent(path);
        _mp4Cache = (path, stamp, content);
        return content;
    }

    private static readonly HashSet<string> Mp4Extensions = new([".mp4", ".m4v", ".mov"], StringComparer.OrdinalIgnoreCase);
    private (string Path, DateTime Stamp, MediaContent Content)? _mp4Cache;

    public async Task PlayAsync(CancellationToken ct = default) => await (await RendererAsync(ct).ConfigureAwait(false)).PlayAsync(ct).ConfigureAwait(false);
    public async Task PauseAsync(CancellationToken ct = default) => await (await RendererAsync(ct).ConfigureAwait(false)).PauseAsync(ct).ConfigureAwait(false);
    public async Task StopAsync(CancellationToken ct = default) => await (await RendererAsync(ct).ConfigureAwait(false)).StopAsync(ct).ConfigureAwait(false);
    public async Task SeekAsync(TimeSpan position, CancellationToken ct = default) =>
        await (await RendererAsync(ct).ConfigureAwait(false)).SeekAsync(position, ct).ConfigureAwait(false);
    public async Task<PlaybackInfo> GetInfoAsync(CancellationToken ct = default) =>
        await (await RendererAsync(ct).ConfigureAwait(false)).GetInfoAsync(ct).ConfigureAwait(false);

    public TimeSpan? BufferedUntil(TimeSpan position, TimeSpan? duration)
    {
        if (_current is not { } c || _server is null) return null;
        var end = _server.SentRanges(c).ContiguousEnd(c.ByteAt(position, duration));
        return c.TimeAt(end, duration);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync().ConfigureAwait(false);
        _server = null;
    }
}
