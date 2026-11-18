using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using HisenseTvRemote.Core.Dlna;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.ViewModels;
using Microsoft.Win32;

namespace HisenseTvRemote;

/// <summary>
/// « Partager vers la TV » : photos, vidéos et musique du PC envoyées à la TV en DLNA. La logique est dans
/// <see cref="ShareViewModel"/> ; ici, seulement les boîtes de dialogue, le glisser-déposer et le clavier.
/// Fermer la fenêtre arrête l'affichage sur la TV et le serveur local.
/// </summary>
public partial class ShareWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly AppServices _app;
    private readonly PhotoPreparer _photos = new();
    private readonly VideoRemuxer? _videos = FfmpegLocator.Find() is { } ff ? new VideoRemuxer(ff.Ffmpeg, ff.Ffprobe) : null;
    private readonly ShareViewModel _vm;
    private readonly CancellationTokenSource _monitor = new();

    public ShareWindow(AppServices app, IEnumerable<string>? files = null)
    {
        InitializeComponent();
        _app = app;
        _vm = new ShareViewModel(CreatePlayer, _photos.PrepareAsync, durationOf: MediaDuration.Of, prepareVideo: _videos is null ? null : _videos.RemuxAsync);
        DataContext = _vm;
        // Un clic sur la piste est traité (Handled) par le Slider lui-même (IsMoveToPointEnabled) : on
        // s'abonne aussi aux événements déjà traités, sinon seul le glissement du curseur déclenchait un saut.
        SeekBar.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekBarDown), handledEventsToo: true);
        SeekBar.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSeekBarUp), handledEventsToo: true);
        TvLabel.Text = $"Vers « {_app.Remote.CurrentTv ?? "?"} » — la TV va chercher les fichiers sur ce PC (réseau local).";
        if (files is not null) AddFiles(files);
    }

    /// <summary>Ajoute des fichiers ou dossiers (glissés sur la fenêtre principale, par exemple).</summary>
    public void AddFiles(IEnumerable<string> paths) => _vm.Add(paths);

    /// <summary>Lecteur DLNA de la TV choisie dans la fenêtre principale.</summary>
    private IMediaPlayer CreatePlayer()
    {
        var tv = TvLookup.Resolve(_app.Tv.TvStore.Load(), _app.Remote.CurrentTv)
                 ?? throw new InvalidOperationException("aucune TV n'est configurée (TV et réglages).");
        if (!IPAddress.TryParse(tv.Host, out var ip))
            throw new InvalidOperationException($"adresse de la TV inconnue ou invalide ({tv.Host}).");
        return new DlnaPlayer(ip, Http);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Pare-feu vérifié d'emblée : sur certains postes, Windows bloque sans rien demander.
        var exe = Environment.ProcessPath;
        if (exe is not null && await Task.Run(() => FirewallHelper.HasAllowRule(exe)) == false)
        {
            _vm.FirewallSuspected = true;
            _vm.Report("Avant de partager : autorisez l'app dans le pare-feu Windows (bandeau ci-dessus).", false);
        }
        await _vm.MonitorAsync(_monitor.Token);
    }

    private async void OnAllowFirewall(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        FirewallButton.IsEnabled = false;
        try
        {
            var error = await Task.Run(() => FirewallHelper.AddRule(exe));
            if (error is null)
            {
                _vm.FirewallSuspected = false;
                _vm.Report("Partage autorisé dans le pare-feu. Envoyez (ou renvoyez) un fichier à la TV.", false);
            }
            else _vm.Report("Pare-feu non modifié : " + error, true);
        }
        finally { FirewallButton.IsEnabled = true; }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _monitor.Cancel();
        await _vm.DisposeAsync();
        _photos.Dispose();
        _videos?.Dispose();
        _monitor.Dispose();
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Photos, vidéos ou musiques à partager", Multiselect = true, Filter = MediaTypes.OpenFileFilter };
        if (dlg.ShowDialog(this) == true) _vm.Add(dlg.FileNames);
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier à partager", Multiselect = true };
        if (dlg.ShowDialog(this) == true) _vm.Add(dlg.FolderNames);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _vm.Add(paths);
    }

    // ---------- Barre de progression : la TV ne saute qu'au relâchement --------------------------

    private void OnSeekBarDown(object sender, MouseButtonEventArgs e) => _vm.IsDragging = true;

    private async void OnSeekBarUp(object sender, MouseButtonEventArgs e) => await EndSeekAsync();

    /// <summary>Relâché hors de la barre pendant un glissement : on saute quand même là où il a été lâché.</summary>
    private async void OnSeekBarLostCapture(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Released) await EndSeekAsync();
    }

    private async Task EndSeekAsync()
    {
        if (!_vm.IsDragging) return;
        _vm.IsDragging = false;
        await _vm.SeekToAsync(SeekBar.Value);
    }

    private async void OnListDoubleClick(object sender, MouseButtonEventArgs e) => await _vm.ShowSelectedAsync();

    /// <summary>
    /// Entrée : envoyer ; ← / → : précédent / suivant ; Ctrl + ← / → : ∓10 s ; Maj + ← / → : ∓30 s ;
    /// Espace : lecture / pause ; Suppr : retirer ; Échap : fermer.
    /// </summary>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Enter: e.Handled = true; await _vm.ShowSelectedAsync(); break;
            case Key.Escape: e.Handled = true; Close(); break;
            case Key.Delete: e.Handled = true; _vm.RemoveSelectedCommand.Execute(null); break;
            case Key.Space when _vm.IsMedia: e.Handled = true; await _vm.PlayPauseAsync(); break;
            case Key.Left or Key.Right:
                e.Handled = true;
                var forward = e.Key == Key.Right;
                if (_vm.IsMedia && mods == ModifierKeys.Control) await _vm.SeekByAsync(forward ? 10 : -10);
                else if (_vm.IsMedia && mods == ModifierKeys.Shift) await _vm.SeekByAsync(forward ? 30 : -30);
                else if (mods == ModifierKeys.None) await (forward ? _vm.NextAsync() : _vm.PreviousAsync());
                break;
        }
    }
}
