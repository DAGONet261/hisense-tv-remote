using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.YouTube;

namespace HisenseTvRemote;

/// <summary>
/// « Envoyer un lien YouTube » : lit (ou ajoute à la file) une vidéo YouTube sur la TV, comme le bouton
/// « caster » de l'app mobile (<see cref="YouTubeCaster"/> : DIAL + API Lounge de YouTube). Un lien
/// YouTube présent dans le presse-papiers est proposé d'office.
/// </summary>
public partial class YouTubeWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    /// <summary>Partagé entre les ouvertures : garde le jeton de salon de la TV.</summary>
    private static readonly YouTubeCaster Caster = new(Http);

    private readonly AppServices _app;

    public YouTubeWindow(AppServices app)
    {
        InitializeComponent();
        _app = app;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText() && YouTubeCaster.ParseVideoId(Clipboard.GetText()) is not null)
            {
                Link.Text = Clipboard.GetText().Trim();
                Report("Lien YouTube repris du presse-papiers. Entrée pour le lire sur la TV.", false);
            }
        }
        catch (System.Runtime.InteropServices.ExternalException) { }             // presse-papiers occupé
        Link.Focus();
        Link.SelectAll();
        UpdateButtons();
    }

    private void OnLinkChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var ok = YouTubeCaster.ParseVideoId(Link.Text) is not null;
        PlayButton.IsEnabled = QueueButton.IsEnabled = ok && Busy.Visibility != Visibility.Visible;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }

    private async void OnPlay(object sender, RoutedEventArgs e) => await SendAsync(queue: false);

    private async void OnQueue(object sender, RoutedEventArgs e) => await SendAsync(queue: true);

    private async Task SendAsync(bool queue)
    {
        if (YouTubeCaster.ParseVideoId(Link.Text) is not { } id)
        {
            Report("Ce n'est pas un lien de vidéo YouTube.", true);
            return;
        }
        var tv = TvLookup.Resolve(_app.Tv.TvStore.Load(), _app.Remote.CurrentTv);
        if (tv is null || !IPAddress.TryParse(tv.Host, out var ip))
        {
            Report("Aucune TV configurée (TV et réglages).", true);
            return;
        }
        Busy.Visibility = Visibility.Visible;
        UpdateButtons();
        Report(queue ? "Ajout à la file de YouTube sur la TV…" : "Ouverture de YouTube sur la TV…", false);
        try
        {
            if (queue) await Caster.QueueAsync(ip, id);
            else await Caster.PlayAsync(ip, id);
            Report(queue ? "Vidéo ajoutée à la file de YouTube sur la TV." : "Vidéo envoyée : YouTube la lit sur la TV.", false);
        }
        catch (YouTubeCastException ex) { Report(ex.Message, true); }
        finally
        {
            Busy.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }
    }

    private void Report(string text, bool failed)
    {
        Status.Text = text;
        Status.Foreground = (System.Windows.Media.Brush)FindResource(failed ? "DangerBrush" : "MutedBrush");
    }
}
