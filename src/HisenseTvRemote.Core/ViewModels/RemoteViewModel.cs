using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HisenseTvRemote.Core.Storage;

namespace HisenseTvRemote.Core.ViewModels;

/// <summary>Une app installée sur la TV, avec son logo officiel (chargé en arrière-plan).</summary>
public sealed partial class AppTile(string name) : ObservableObject
{
    public string Name { get; } = name;
    /// <summary>Lettre affichée tant que le logo n'est pas disponible.</summary>
    public string Initial { get; } = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";

    [ObservableProperty]
    private string? _iconPath;
}

/// <summary>Une touche affichée avec son nom français (le nom technique sert d'info-bulle).</summary>
public sealed record KeyItem(string Key, string Label);

/// <summary>Une entrée de la TV : <see cref="Id"/> est envoyé à <c>source</c>, <see cref="Label"/> est affiché.</summary>
public sealed record SourceTile(string Id, string Label);

/// <summary>
/// Écran principal (télécommande, apps, sources). Toute la logique est ici ; la vue ne fait que lier.
/// Les commandes de touches acceptent l'exécution concurrente : un clic n'attend pas le précédent
/// (TvController sérialise de toute façon les envois vers la TV).
/// </summary>
public sealed partial class RemoteViewModel : ObservableObject
{
    private readonly TvController _tv;
    private readonly IconCache? _icons;
    private readonly Action<string?> _onTvChanged;
    private CancellationTokenSource? _volumeRefresh;
    private int _busy;
    private bool _reloading;

    public RemoteViewModel(TvController tv, IconCache? icons, string? initialTv, Action<string?>? onTvChanged = null)
    {
        _tv = tv;
        _icons = icons;
        _onTvChanged = onTvChanged ?? (_ => { });
        _currentTv = initialTv;
        foreach (var app in KeyCatalog.Apps) Apps.Add(new AppTile(app));
        foreach (var s in KeyCatalog.Sources) Sources.Add(new SourceTile(s, s.ToUpperInvariant()));
    }

    public ObservableCollection<string> Tvs { get; } = [];
    public ObservableCollection<AppTile> Apps { get; } = [];
    public ObservableCollection<SourceTile> Sources { get; } = [];

    /// <summary>Pavé numérique, dans l'ordre d'une télécommande (1-9 puis 0).</summary>
    public IReadOnlyList<KeyItem> DigitKeys { get; } =
        [.. "123456789".Select(c => new KeyItem(c.ToString(), c.ToString())), new KeyItem("0", "0")];

    /// <summary>Touches secondaires, regroupées dans « Plus de touches ».</summary>
    public IReadOnlyList<KeyItem> ExtraKeys { get; } =
        [.. new[] { "menu", "exit", "channeldot", "zoomin", "zoomout", "ok_long_press", "mute_long_press",
                    "leftmousekeys", "uddleftmousekeys", "uduleftmousekeys", "voicedown", "voiceup" }
            .Select(k => new KeyItem(k, KeyCatalog.Label(k)))];

    [ObservableProperty]
    private string? _currentTv;

    [ObservableProperty]
    private string _status = "Prêt";

    /// <summary>La dernière commande a échoué (la barre d'état passe en rouge).</summary>
    [ObservableProperty]
    private bool _lastFailed;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Niveau de volume connu (null si inconnu).</summary>
    [ObservableProperty]
    private int? _volume;

    [ObservableProperty]
    private bool _isLoadingApps;

    partial void OnCurrentTvChanged(string? value)
    {
        if (_reloading) return;
        _onTvChanged(value);
        _ = LoadTvContentAsync();
    }

    /// <summary>Relit la liste des TV (base ou config.yaml) ; garde la TV courante si elle existe encore.</summary>
    public void ReloadTvs()
    {
        var tvs = _tv.TvStore.Load();
        _reloading = true;
        try
        {
            Tvs.Clear();
            foreach (var t in tvs) Tvs.Add(t.Key);
            if (CurrentTv is null || !Tvs.Contains(CurrentTv))
                CurrentTv = tvs.FirstOrDefault(t => t.IsDefault)?.Key ?? tvs.FirstOrDefault()?.Key;
        }
        finally { _reloading = false; }
        _onTvChanged(CurrentTv);
        if (tvs.Count == 0) Report("Aucune TV configurée — ⚙ TV et réglages : rechercher puis ajouter.", failed: true);
    }

    /// <summary>Apps, sources et volume de la TV courante.</summary>
    public async Task LoadTvContentAsync()
    {
        if (CurrentTv is null) return;
        await RefreshApps();
        await RefreshSources();
        await RefreshVolumeNowAsync();
    }

    // ------------------------------------------------------------- exécution -------------

    /// <summary>Exécute une commande, met à jour la barre d'état ; ne lève jamais.</summary>
    public async Task<CommandResult> Run(Func<Task<CommandResult>> action)
    {
        if (Interlocked.Increment(ref _busy) == 1) IsBusy = true;
        try
        {
            var r = await action();
            var summary = r.Summary;
            if (!r.Success && LooksUnreachable(r)) summary += "  →  TV injoignable ? ⚙ : « Retrouver l'IP ».";
            Report(r.Success ? summary : $"Code {(r.TimedOut ? "timeout" : r.ExitCode)} — {summary}", !r.Success);
            return r;
        }
        finally
        {
            if (Interlocked.Decrement(ref _busy) == 0) IsBusy = false;
        }
    }

    public void Report(string text, bool failed = false)
    {
        Status = text;
        LastFailed = failed;
    }

    private static bool LooksUnreachable(CommandResult r)
    {
        var t = r.StandardOutput + r.StandardError;
        return r.TimedOut || t.Contains("Failed to connect") || t.Contains("timed out") || t.Contains("Could not detect protocol");
    }

    // ------------------------------------------------------------- télécommande ----------

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Key(string key) => Run(() => _tv.KeyAsync(CurrentTv, key));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ChangeVolume(string direction)
    {
        var up = direction == "up";
        // Affichage immédiat (la TV confirme ensuite) : la molette doit donner un retour sans attendre.
        if (Volume is { } v) Volume = Math.Clamp(v + (up ? 1 : -1), 0, 100);
        var r = await Run(() => _tv.VolumeAsync(CurrentTv, up));
        if (r.Success) ScheduleVolumeRefresh();
    }

    // ------------------------------------------------------------- saisie de texte ------

    private bool _pumping;

    /// <summary>
    /// Texte du champ « Saisie de texte », recopié dans le champ actif de la TV (recherche YouTube…).
    /// Envoyé en entier (« replace ») : le Retour arrière et les corrections fonctionnent naturellement.
    /// </summary>
    [ObservableProperty]
    private string _typedText = "";

    /// <summary>
    /// Chaque lettre part tout de suite, comme un clavier branché sur la TV. Si des lettres arrivent pendant
    /// un envoi, seul le dernier état est envoyé ensuite : la TV rattrape sans file d'attente.
    /// </summary>
    partial void OnTypedTextChanged(string value) => _ = PumpTextAsync();

    private async Task PumpTextAsync()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (TypedText != _lastSentText)
                if (!(await SendTypedTextAsync()).Success) break;
        }
        finally { _pumping = false; }
    }

    /// <summary>Envoie tout de suite le texte saisi (vide = efface le champ de la TV).</summary>
    public async Task<CommandResult> SendTypedTextAsync()
    {
        var text = TypedText;
        var r = await Run(() => _tv.TextAsync(CurrentTv, text, text.Length == 0 ? "clear" : "replace"));
        if (r.Success) _lastSentText = text;
        return r;
    }

    private string? _lastSentText;

    /// <summary>Fin de saisie : envoie seulement si le texte a changé depuis le dernier envoi réussi.</summary>
    public async Task FlushTypedTextAsync()
    {
        if (TypedText != _lastSentText) await SendTypedTextAsync();
    }

    /// <summary>Vide le champ local et celui de la TV.</summary>
    [RelayCommand]
    private void ClearText() => TypedText = "";

    // ------------------------------------------------------------- raccourcis clavier ---

    private bool _playing = true;

    /// <summary>
    /// Exécute un raccourci « TV » (touche, volume, muet, lecture/pause, actualisation). Les raccourcis
    /// d'ouverture de fenêtre (journal, réglages, aide) sont gérés par la vue : renvoie false pour eux.
    /// </summary>
    public async Task<bool> RunShortcutAsync(Shortcut s)
    {
        switch (s.Command)
        {
            case ShortcutCommand.TvKey: await Key(s.TvKey!); return true;
            case ShortcutCommand.VolumeUp: await ChangeVolume("up"); return true;
            case ShortcutCommand.VolumeDown: await ChangeVolume("down"); return true;
            case ShortcutCommand.Mute: await Mute(); return true;
            case ShortcutCommand.PlayPause:
                // Une seule touche côté clavier, deux côté TV : on alterne (en commençant par pause).
                var key = _playing ? "pause" : "play";
                _playing = !_playing;
                await Key(key);
                return true;
            case ShortcutCommand.RefreshLists:
                await RefreshApps();
                await RefreshSources();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Cran standard de la molette Windows (WHEEL_DELTA).</summary>
    public const int WheelNotch = 120;

    private int _wheelRemainder;

    /// <summary>
    /// Molette de la souris : un cran (<see cref="WheelNotch"/>) = un pas de volume, vers le haut = plus fort.
    /// Les pavés tactiles envoient des fractions de cran : elles sont cumulées.
    /// </summary>
    public async Task WheelVolumeAsync(int delta)
    {
        _wheelRemainder += delta;
        var steps = _wheelRemainder / WheelNotch;
        if (steps == 0) return;
        _wheelRemainder -= steps * WheelNotch;
        for (var i = 0; i < Math.Abs(steps); i++)
            await ChangeVolume(steps > 0 ? "up" : "down");
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Mute()
    {
        var r = await Run(() => _tv.KeyAsync(CurrentTv, "mute"));
        if (r.Success) ScheduleVolumeRefresh();
    }

    /// <summary>wake | on | off.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Power(string action) => Run(() => action switch
    {
        "wake" => _tv.WakeAsync(CurrentTv),
        "on" => _tv.OnAsync(CurrentTv),
        "off" => _tv.OffAsync(CurrentTv),
        _ => _tv.KeyAsync(CurrentTv, "power"),
    });

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LaunchApp(AppTile app) => Run(() => _tv.AppAsync(CurrentTv, app.Name));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SelectSource(SourceTile source) => Run(() => _tv.SourceAsync(CurrentTv, source.Id));

    // ------------------------------------------------------------- apps / sources --------

    [RelayCommand]
    private async Task RefreshApps()
    {
        IsLoadingApps = true;
        try
        {
            var r = await Run(() => _tv.AppListAsync(CurrentTv));
            if (!r.Success || _tv.Engine.AppsOf(CurrentTv) is not { } raw) return;
            var names = raw.Select(a => (Name: a["name"]?.ToString(), Icon: a["httpIcon"]?.ToString()))
                           .Where(a => !string.IsNullOrWhiteSpace(a.Name)).ToList();
            if (names.Count == 0) return;

            Apps.Clear();
            var loads = new List<Task>();
            foreach (var (name, icon) in names)
            {
                var tile = new AppTile(name!);
                Apps.Add(tile);
                if (_icons is not null && icon is not null) loads.Add(LoadIconAsync(tile, icon));
            }
            Report($"{Apps.Count} applications sur « {CurrentTv} ».");
            await Task.WhenAll(loads);
        }
        finally { IsLoadingApps = false; }
    }

    private async Task LoadIconAsync(AppTile tile, string httpIcon) =>
        tile.IconPath = await _icons!.GetAsync(httpIcon);

    [RelayCommand]
    private async Task RefreshSources()
    {
        var r = await Run(() => _tv.SourceListAsync(CurrentTv));
        if (!r.Success || _tv.Engine.SourcesOf(CurrentTv) is not { } raw) return;
        var list = raw.Select(s => new SourceTile(
                          s["sourceid"]?.ToString() ?? "",
                          CleanLabel(s["displayname"]?.ToString() ?? s["sourcename"]?.ToString() ?? "?")))
                      .Where(s => s.Id.Length > 0).ToList();
        if (list.Count == 0) return;
        Sources.Clear();
        foreach (var s in list) Sources.Add(s);
        Report($"{Sources.Count} sources sur « {CurrentTv} ».");
    }

    /// <summary>La TV ajoute des décorations aux noms (« TV Channels▶ »).</summary>
    internal static string CleanLabel(string s) => s.Trim().TrimEnd('▶', '►', ' ');

    // ------------------------------------------------------------- volume ----------------

    /// <summary>Relit le niveau peu après une rafale de clics (une seule requête pour la rafale).</summary>
    private void ScheduleVolumeRefresh()
    {
        _volumeRefresh?.Cancel();
        var cts = _volumeRefresh = new CancellationTokenSource();
        _ = Task.Delay(500, cts.Token).ContinueWith(async t =>
        {
            if (!t.IsCanceled) await RefreshVolumeNowAsync();
        }, CancellationToken.None, TaskContinuationOptions.None, SyncOrDefault());
    }

    private static TaskScheduler SyncOrDefault() =>
        SynchronizationContext.Current is not null ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Default;

    private async Task RefreshVolumeNowAsync()
    {
        var r = await _tv.VolumeGetAsync(CurrentTv);
        var line = r.StandardOutput.Trim();
        Volume = line.StartsWith("Volume:", StringComparison.Ordinal) && int.TryParse(line[7..].Trim(), out var v) ? v : Volume;
    }
}
