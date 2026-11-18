using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Runtime.InteropServices;
using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Vidaa;

namespace HisenseTvRemote;

/// <summary>
/// Télécommande, apps et sources. La logique est dans <see cref="Core.ViewModels.RemoteViewModel"/> ;
/// ce code ne gère que la vue (menu de choix de TV, ouverture des autres fenêtres, démarrage).
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppServices _app;
    private TvWindow? _tvWindow;
    private JournalWindow? _journalWindow;
    private ShareWindow? _shareWindow;
    private YouTubeWindow? _youTubeWindow;

    public MainWindow()
    {
        InitializeComponent();
        try { _app = AppServices.Create(); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Impossible d'ouvrir la base de l'application :\n{ex.Message}", "VIDAA - Hisense",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            _app = null!;
            return;
        }
        DataContext = _app.Remote;
        // Le journal reçoit chaque commande, quel que soit le thread émetteur.
        _app.Tv.CommandCompleted += r => Dispatcher.BeginInvoke(() => _app.Record(r));

        Loaded += async (_, _) =>
        {
            _app.Remote.ReloadTvs();
            if (!CertificateOk()) return;
            await _app.Remote.LoadTvContentAsync();
        };
        Closed += (_, _) => _app.Dispose();
    }

    /// <summary>Sans certificat client, la TV refuse la connexion : on le signale d'emblée.</summary>
    private bool CertificateOk()
    {
        if (ClientCertificate.Find(_app.Settings.CertDirectory) is not null) return true;
        _app.Remote.Report("Certificat client VIDAA absent : ⚙ TV et réglages → « Importer le certificat (.p12) ».", failed: true);
        return false;
    }

    // ---------- Raccourcis clavier ------------------------------------------------------------

    /// <summary>
    /// Intercepté avant les boutons : sinon Entrée/Espace « cliqueraient » le bouton qui a le focus et
    /// les flèches déplaceraient le focus au lieu de piloter la TV. Table : <see cref="KeyboardShortcuts"/>.
    /// </summary>
    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        _swallowText = false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = KeyMods.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyMods.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyMods.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= KeyMods.Alt;

        if (TypingBox.IsKeyboardFocusWithin)
        {
            // Pendant la saisie : Entrée/Échap terminent (les flèches pilotent de nouveau la TV) ;
            // les autres touches sans Ctrl appartiennent au texte (flèches, Espace, Retour arrière…).
            if (key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                EndTyping();
                return;
            }
            if (!mods.HasFlag(KeyMods.Ctrl)) return;
        }

        if (KeyboardShortcuts.Match(key.ToString(), CharOf(key), mods) is not { } shortcut) return;

        e.Handled = true;
        _swallowText = true; // Espace, pavé numérique… : pas de caractère dans la saisie ensuite
        switch (shortcut.Command)
        {
            case ShortcutCommand.OpenJournal: OnOpenJournal(this, e); break;
            case ShortcutCommand.OpenSettings: OnOpenTvWindow(this, e); break;
            case ShortcutCommand.ShowHelp: OnShowShortcuts(this, e); break;
            case ShortcutCommand.OpenMouseMode: OnOpenMouseMode(this, e); break;
            case ShortcutCommand.OpenShare: OnOpenShare(this, e); break;
            case ShortcutCommand.OpenYouTube: OnOpenYouTube(this, e); break;
            default: await _app.Remote.RunShortcutAsync(shortcut); break;
        }
    }

    /// <summary>Vrai juste après un raccourci : son caractère éventuel ne doit pas démarrer une saisie.</summary>
    private bool _swallowText;

    /// <summary>
    /// Une lettre, un chiffre de la rangée du haut ou un symbole tapé hors du champ démarre une nouvelle
    /// saisie : le champ prend le focus et le texte part vers la TV. Maj et Verr. Maj s'appliquent comme
    /// d'habitude (AZERTY : chiffres avec Verr. Maj).
    /// </summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (_swallowText) { _swallowText = false; e.Handled = true; return; }
        if (TypingBox.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        e.Handled = true;
        _app.Remote.TypedText = e.Text;           // nouvelle recherche
        TypingBox.Focus();
        TypingBox.CaretIndex = TypingBox.Text.Length;
    }

    /// <summary>Fin de saisie : envoi immédiat du texte, puis le clavier pilote de nouveau la TV.</summary>
    private async void EndTyping()
    {
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
        Focus();
        await _app.Remote.FlushTypedTextAsync();
        _app.Remote.Report($"Texte envoyé à la TV : « {_app.Remote.TypedText} ». Flèches + Entrée pour choisir un résultat.");
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>Caractère produit par la touche (sans Maj) dans la disposition de clavier courante.</summary>
    private static char? CharOf(Key key)
    {
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var c = MapVirtualKey(vk, 2) & 0x7FFF; // MAPVK_VK_TO_CHAR (bit haut = touche morte)
        return c == 0 ? null : char.ToLowerInvariant((char)c);
    }

    private void OnOpenMouseMode(object sender, RoutedEventArgs e)
    {
        if (TypingBox.IsKeyboardFocusWithin) EndTyping();
        new MouseModeWindow(_app.Remote, _app.Settings) { Owner = this }.Show();
    }

    // ---------- Partager vers la TV (DLNA) -----------------------------------------------------

    private void OnOpenShare(object sender, RoutedEventArgs e) => OpenShare(null);

    /// <summary>« Envoyer un lien YouTube » (une seule fenêtre ; ramenée devant si déjà ouverte).</summary>
    private void OnOpenYouTube(object sender, RoutedEventArgs e)
    {
        if (TypingBox.IsKeyboardFocusWithin) EndTyping();
        if (_youTubeWindow is { IsLoaded: true }) { _youTubeWindow.Activate(); return; }
        _youTubeWindow = new YouTubeWindow(_app) { Owner = this };
        _youTubeWindow.Show();
    }

    /// <summary>Ouvre (ou ramène) « Partager vers la TV », en y ajoutant éventuellement des fichiers.</summary>
    private void OpenShare(string[]? files)
    {
        if (TypingBox.IsKeyboardFocusWithin) EndTyping();
        if (_shareWindow is { IsLoaded: true })
        {
            if (files is not null) _shareWindow.AddFiles(files);
            _shareWindow.Activate();
            return;
        }
        _shareWindow = new ShareWindow(_app, files) { Owner = this };
        _shareWindow.Show();
    }

    private void OnDragOverFiles(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Fichiers glissés sur la télécommande : ouverts dans « Partager vers la TV ».</summary>
    private void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) OpenShare(paths);
    }

    private void OnShowShortcuts(object sender, RoutedEventArgs e) =>
        new ShortcutsWindow { Owner = this }.ShowDialog();

    // ---------- Molette : volume -------------------------------------------------------------

    /// <summary>Molette au-dessus de la bascule Volume.</summary>
    private async void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        await _app.Remote.WheelVolumeAsync(e.Delta);
    }

    /// <summary>Ctrl + molette n'importe où dans la fenêtre (sans Ctrl, la molette fait défiler les listes).</summary>
    private async void OnWindowWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        await _app.Remote.WheelVolumeAsync(e.Delta);
    }

    /// <summary>Menu des TV configurées sous le bouton de choix.</summary>
    private void OnPickTv(object sender, RoutedEventArgs e)
    {
        _app.Remote.ReloadTvs();
        var menu = new ContextMenu { PlacementTarget = TvPicker, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var tv in _app.Remote.Tvs)
        {
            var item = new MenuItem { Header = tv, IsCheckable = true, IsChecked = tv == _app.Remote.CurrentTv };
            item.Click += (_, _) => _app.Remote.CurrentTv = tv;
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "Gérer les TV…" };
        manage.Click += OnOpenTvWindow;
        menu.Items.Add(manage);
        menu.IsOpen = true;
    }

    private void OnOpenTvWindow(object sender, RoutedEventArgs e)
    {
        if (_tvWindow is { IsLoaded: true }) { _tvWindow.Activate(); return; }
        _tvWindow = new TvWindow(_app) { Owner = this };
        _tvWindow.Show();
    }

    private void OnOpenJournal(object sender, RoutedEventArgs e)
    {
        if (_journalWindow is { IsLoaded: true }) { _journalWindow.Activate(); return; }
        _journalWindow = new JournalWindow(_app) { Owner = this };
        _journalWindow.Show();
    }
}
