using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HisenseTvRemote.Core.Dlna;

namespace HisenseTvRemote.Core.ViewModels;

/// <summary>Un fichier de la liste de partage.</summary>
public sealed partial class ShareItem(string path, MediaType type) : ObservableObject
{
    public string Path { get; } = path;
    public MediaType Type { get; } = type;
    public MediaKind Kind => Type.Kind;
    public string Name { get; } = System.IO.Path.GetFileName(path);

    /// <summary>Icône Segoe Fluent : photo, vidéo, musique.</summary>
    public string Glyph => Kind switch { MediaKind.Photo => "", MediaKind.Video => "", _ => "" };

    public string KindLabel => Kind switch { MediaKind.Photo => "Photo", MediaKind.Video => "Vidéo", _ => "Musique" };

    public string SizeText { get; } = FormatSize(path);

    /// <summary>Fichier actuellement affiché ou joué sur la TV.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    private static string FormatSize(string path)
    {
        try
        {
            var b = new FileInfo(path).Length;
            return b >= 1024 * 1024 * 1024 ? (b / 1024d / 1024 / 1024).ToString("0.0 Go", CultureInfo.GetCultureInfo("fr-FR"))
                : b >= 1024 * 1024 ? (b / 1024d / 1024).ToString("0.0 Mo", CultureInfo.GetCultureInfo("fr-FR"))
                : (b / 1024d).ToString("0 Ko", CultureInfo.GetCultureInfo("fr-FR"));
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
}

/// <summary>
/// Écran « Partager vers la TV » : liste de photos, vidéos et musiques envoyées à la TV par DLNA, avec
/// suivant/précédent, diaporama, lecture/pause/stop, avance/recul et enchaînement automatique.
/// Le lecteur est créé au premier envoi (<paramref name="playerFactory"/>) ; les photos passent par
/// <paramref name="preparePhoto"/> (conversion HEIC, orientation, taille) avant l'envoi. La durée des
/// vidéos vient de <paramref name="durationOf"/> : la 55A53FXVT renvoie toujours 0 (TrackDuration).
/// </summary>
public sealed partial class ShareViewModel(
    Func<IMediaPlayer> playerFactory,
    Func<string, CancellationToken, Task<string>>? preparePhoto = null,
    Func<DateTime>? clock = null,
    Func<string, TimeSpan?>? durationOf = null) : ObservableObject, IAsyncDisposable
{
    /// <summary>Sans signe de lecture de la TV au bout de ce délai, le diaporama démarre quand même.</summary>
    private static readonly TimeSpan DisplayFallback = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);
    /// <summary>Sans téléchargement par la TV au bout de ce délai : pare-feu probable.</summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private IMediaPlayer? _player;
    private int _busy;
    private DateTime _sentAt;
    private DateTime? _displayedAt;
    private DateTime _lastPoll = DateTime.MinValue;
    private bool _sawPlaying;
    private bool _userStopped;
    private volatile bool _fetched;
    /// <summary>Après un saut, la TV renvoie encore l'ancienne position un court instant : on l'ignore.</summary>
    private DateTime _ignorePositionUntil;
    private bool _fetchChecked;

    public ObservableCollection<ShareItem> Items { get; } = [];

    /// <summary>Intervalles proposés pour le diaporama (secondes).</summary>
    public IReadOnlyList<int> SlideshowChoices { get; } = [3, 5, 8, 10, 15, 30, 60];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMedia), nameof(IsPhoto), nameof(HasCurrent))]
    private ShareItem? _current;

    [ObservableProperty]
    private ShareItem? _selected;

    [ObservableProperty]
    private string _status = "Ajoutez des photos, vidéos ou musiques, puis double-cliquez pour les envoyer à la TV.";

    [ObservableProperty]
    private bool _lastFailed;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _positionText = "";

    /// <summary>Barre de progression : position (secondes), suivie de la TV sauf pendant un glissement.</summary>
    [ObservableProperty]
    private double _positionSeconds;

    /// <summary>Durée de la vidéo/musique (secondes) ; 0 si la TV ne l'indique pas encore.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText), nameof(CanSeekBar))]
    private double _durationSeconds;

    /// <summary>Partie déjà chargée par la TV (secondes), en avance sur la lecture : couche claire de la barre.</summary>
    [ObservableProperty]
    private double _bufferedSeconds;

    /// <summary>L'utilisateur fait glisser la barre : la position de la TV n'est plus recopiée.</summary>
    public bool IsDragging { get; set; }

    public string ElapsedText => Short(TimeSpan.FromSeconds(PositionSeconds));
    public string DurationText => DurationSeconds > 0 ? Short(TimeSpan.FromSeconds(DurationSeconds)) : "--:--";
    public bool CanSeekBar => DurationSeconds > 0;

    partial void OnPositionSecondsChanged(double value) => OnPropertyChanged(nameof(ElapsedText));

    /// <summary>
    /// Le pare-feu Windows empêche sans doute la TV de télécharger les fichiers : la vue propose de
    /// l'autoriser (règle « réseau privé, sous-réseau local »).
    /// </summary>
    [ObservableProperty]
    private bool _firewallSuspected;

    [ObservableProperty]
    private bool _slideshow;

    [ObservableProperty]
    private int _slideshowSeconds = 5;

    /// <summary>Vidéo ou musique en cours : commandes de lecture et d'avance/recul visibles.</summary>
    public bool IsMedia => Current is { Kind: not MediaKind.Photo };
    public bool IsPhoto => Current is { Kind: MediaKind.Photo };
    public bool HasCurrent => Current is not null;
    public bool HasItems => Items.Count > 0;

    // ---------- Liste ----------------------------------------------------------------------------

    /// <summary>Ajoute les fichiers pris en charge (les dossiers : leurs fichiers, triés par nom). Renvoie (ajoutés, ignorés).</summary>
    public (int Added, int Ignored) Add(IEnumerable<string> paths)
    {
        int added = 0, ignored = 0;
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var (a, i) = Add(Directory.EnumerateFiles(p).Order(StringComparer.CurrentCultureIgnoreCase));
                added += a; ignored += i;
                continue;
            }
            var type = MediaTypes.Of(p);
            if (type is null || !File.Exists(p)) { ignored++; continue; }
            if (Items.Any(x => string.Equals(x.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            Items.Add(new ShareItem(p, type));
            added++;
        }
        OnPropertyChanged(nameof(HasItems));
        if (added > 0 || ignored > 0)
            Report(ignored == 0 ? $"{added} fichier(s) ajouté(s). Double-cliquez sur un fichier pour l'envoyer à la TV."
                : $"{added} fichier(s) ajouté(s), {ignored} ignoré(s) (format non pris en charge).", false);
        Selected ??= Items.FirstOrDefault();
        return (added, ignored);
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Selected is null) return;
        var i = Items.IndexOf(Selected);
        if (ReferenceEquals(Selected, Current)) Current = null;
        Items.Remove(Selected);
        Selected = Items.Count == 0 ? null : Items[Math.Min(i, Items.Count - 1)];
        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    private void Clear()
    {
        Items.Clear();
        Current = null;
        Selected = null;
        Slideshow = false;
        OnPropertyChanged(nameof(HasItems));
    }

    // ---------- Envoi à la TV --------------------------------------------------------------------

    [RelayCommand]
    public Task ShowSelectedAsync() => Selected is null ? Task.CompletedTask : ShowAsync(Selected);

    /// <summary>Envoie <paramref name="item"/> à la TV (affichage ou lecture).</summary>
    public async Task ShowAsync(ShareItem item)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        IsBusy = true;
        try
        {
            if (Current is not null) Current.IsCurrent = false;
            Current = item;
            Selected = item;
            item.IsCurrent = true;
            IsPlaying = false;
            PositionText = "";
            PositionSeconds = 0;
            DurationSeconds = 0;
            BufferedSeconds = 0;
            _sawPlaying = false;
            _userStopped = false;
            _displayedAt = null;
            _fetched = false;
            _fetchChecked = false;
            Report($"Envoi de « {item.Name} » à la TV…", false);

            var duration = item.Kind == MediaKind.Photo ? null : durationOf?.Invoke(item.Path);
            if (duration is { } known && known > TimeSpan.Zero) DurationSeconds = known.TotalSeconds;
            var path = item.Path;
            if (item.Kind == MediaKind.Photo && preparePhoto is not null)
                path = await preparePhoto(item.Path, CancellationToken.None);
            if (_player is null)
            {
                _player = playerFactory();
                _player.RequestServed += _ => _fetched = true;
            }
            await _player.ShowAsync(path, item.Type, Path.GetFileNameWithoutExtension(item.Name), duration);
            _sentAt = _now();
            Report(item.Kind == MediaKind.Photo
                ? $"« {item.Name} » envoyée : la TV l'affiche (quelques secondes la première fois)."
                : $"« {item.Name} » envoyée : la TV lance la lecture.", false);
        }
        catch (Exception ex) when (ex is DlnaException or IOException or UnauthorizedAccessException or InvalidOperationException
                                       or NotSupportedException or System.Net.Sockets.SocketException or HttpRequestException)
        {
            Report($"Impossible d'envoyer « {item.Name} » : {ex.Message}", true);
            Slideshow = false;
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    [RelayCommand]
    public Task NextAsync() => MoveAsync(+1);

    [RelayCommand]
    public Task PreviousAsync() => MoveAsync(-1);

    /// <summary>Suivant/précédent dans la liste (en boucle), à partir du fichier en cours ou sélectionné.</summary>
    private Task MoveAsync(int step)
    {
        if (Items.Count == 0) return Task.CompletedTask;
        var from = Current ?? Selected;
        var i = from is null ? (step > 0 ? -1 : 0) : Items.IndexOf(from);
        var next = Items[((i + step) % Items.Count + Items.Count) % Items.Count];
        return ShowAsync(next);
    }

    // ---------- Lecture ---------------------------------------------------------------------------

    [RelayCommand]
    public Task PlayPauseAsync() => WithPlayerAsync(IsPlaying ? "Pause" : "Lecture", async p =>
    {
        if (IsPlaying) { await p.PauseAsync(); IsPlaying = false; }
        else { await p.PlayAsync(); IsPlaying = true; _userStopped = false; }
    });

    [RelayCommand]
    public Task StopAsync() => WithPlayerAsync("Stop", async p =>
    {
        _userStopped = true;
        Slideshow = false;
        await p.StopAsync();
        IsPlaying = false;
        Report("Lecture arrêtée sur la TV.", false);
    });

    /// <summary>Avance (secondes &gt; 0) ou recule dans la vidéo/musique en cours.</summary>
    [RelayCommand]
    public Task SeekByAsync(int seconds) => WithPlayerAsync(seconds < 0 ? "Recul" : "Avance", async p =>
    {
        var info = await p.GetInfoAsync();
        if (info.Position is not { } pos) { Report("La TV n'indique pas la position : impossible d'avancer ou de reculer.", true); return; }
        var target = pos + TimeSpan.FromSeconds(seconds);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (info.Duration is { } d && d > TimeSpan.Zero && target > d - TimeSpan.FromSeconds(1)) target = d - TimeSpan.FromSeconds(1);
        await p.SeekAsync(target);
        ShowSeek(target, info.Duration);
        Report($"{(seconds < 0 ? "Recul" : "Avance")} de {Math.Abs(seconds)} s ({DlnaRenderer.FormatTime(target)}).", false);
    });

    /// <summary>Clic ou glissement sur la barre de progression : aller à <paramref name="seconds"/>.</summary>
    public Task SeekToAsync(double seconds) => WithPlayerAsync("Déplacement", async p =>
    {
        var target = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (DurationSeconds > 1 && target.TotalSeconds > DurationSeconds - 1) target = TimeSpan.FromSeconds(DurationSeconds - 1);
        await p.SeekAsync(target);
        ShowSeek(target, DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds) : null);
        Report($"Lecture à {DlnaRenderer.FormatTime(target)}.", false);
    });

    private void ShowSeek(TimeSpan target, TimeSpan? duration)
    {
        PositionSeconds = target.TotalSeconds;
        PositionText = FormatPosition(target, duration);
        _ignorePositionUntil = _now() + TimeSpan.FromSeconds(2);
    }

    private async Task WithPlayerAsync(string what, Func<IMediaPlayer, Task> action)
    {
        if (_player is null || Current is null) return;
        try { await action(_player); }
        catch (DlnaException ex) { Report($"{what} : {ex.Message}", true); }
    }

    // ---------- Suivi : diaporama, position, enchaînement -----------------------------------------

    /// <summary>Appelé régulièrement (toutes les ~0,5 s) par <see cref="MonitorAsync"/>.</summary>
    public async Task TickAsync()
    {
        if (_player is null || Current is null || Volatile.Read(ref _busy) == 1) return;
        var now = _now();

        // La TV n'est pas venue chercher le fichier : le pare-feu bloque presque toujours.
        if (!_fetchChecked && !_fetched && now - _sentAt >= FetchTimeout)
        {
            _fetchChecked = true;
            FirewallSuspected = true;
            Slideshow = false;
            Report("La TV n'est pas venue chercher le fichier : le pare-feu Windows bloque sans doute le partage. " +
                   "Cliquez sur « Autoriser dans le pare-feu », puis renvoyez le fichier.", true);
            return;
        }
        if (_fetched) _fetchChecked = true;

        if (now - _lastPoll >= PollEvery)
        {
            _lastPoll = now;
            PlaybackInfo info;
            try { info = await _player.GetInfoAsync(); }
            catch (DlnaException) { return; }       // TV momentanément muette : on réessaiera
            if (Current is null) return;
            if (info.IsPlaying)
            {
                _sawPlaying = true;
                _displayedAt ??= now;
            }
            IsPlaying = info.IsPlaying;
            // Durée : celle de la TV si elle la donne, sinon celle lue par l'app (la 55A53FXVT renvoie 0).
            if (IsMedia && info.Duration is { } d && d > TimeSpan.Zero) DurationSeconds = d.TotalSeconds;
            if (IsMedia && !IsDragging && now >= _ignorePositionUntil)
            {
                PositionText = info.Position is { } p ? FormatPosition(p, DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds) : info.Duration) : "";
                PositionSeconds = info.Position?.TotalSeconds ?? 0;
            }
            var length = DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds) : info.Duration;
            if (IsMedia && _player.BufferedUntil(TimeSpan.FromSeconds(PositionSeconds), length) is { } loaded)
                BufferedSeconds = Math.Max(PositionSeconds, Math.Min(loaded.TotalSeconds, DurationSeconds > 0 ? DurationSeconds : loaded.TotalSeconds));

            // Fin d'une vidéo/musique (et pas un Stop demandé) : fichier suivant, s'il y en a un.
            if (IsMedia && _sawPlaying && info.IsStopped && !_userStopped)
            {
                _sawPlaying = false;
                var i = Items.IndexOf(Current);
                if (i >= 0 && i < Items.Count - 1) { await ShowAsync(Items[i + 1]); return; }
                Report("Lecture terminée.", false);
            }
        }

        if (Slideshow && IsPhoto)
        {
            var shownSince = _displayedAt ?? (now - _sentAt >= DisplayFallback ? _sentAt + DisplayFallback : (DateTime?)null);
            if (shownSince is { } t && now - t >= TimeSpan.FromSeconds(SlideshowSeconds)) await NextAsync();
        }
    }

    /// <summary>Boucle de suivi, tant que l'écran est ouvert.</summary>
    public async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return; }
            await TickAsync();
        }
    }

    /// <summary>Diaporama plus lent / plus rapide : intervalle suivant ou précédent de <see cref="SlideshowChoices"/>.</summary>
    [RelayCommand]
    private void SlowerSlideshow() =>
        SlideshowSeconds = SlideshowChoices.FirstOrDefault(s => s > SlideshowSeconds, SlideshowChoices[^1]);

    [RelayCommand]
    private void FasterSlideshow() =>
        SlideshowSeconds = SlideshowChoices.LastOrDefault(s => s < SlideshowSeconds, SlideshowChoices[0]);

    partial void OnSlideshowChanged(bool value)
    {
        if (value && Current is null && Items.Count > 0) _ = ShowAsync(Selected ?? Items[0]);
        else if (value) _displayedAt = _now();     // le décompte repart de maintenant
    }

    public static string FormatPosition(TimeSpan position, TimeSpan? duration) =>
        duration is { } d && d > TimeSpan.Zero ? $"{Short(position)} / {Short(d)}" : Short(position);

    private static string Short(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    public void Report(string message, bool failed)
    {
        Status = message;
        LastFailed = failed;
    }

    /// <summary>Arrête la lecture sur la TV (si on avait envoyé quelque chose) et le serveur local.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_player is null) return;
        if (Current is not null)
        {
            try { await _player.StopAsync(); } catch (DlnaException) { }
        }
        await _player.DisposeAsync();
        _player = null;
    }
}
