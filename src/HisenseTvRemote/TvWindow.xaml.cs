using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.Vidaa;
using Microsoft.Win32;

namespace HisenseTvRemote;

/// <summary>
/// Gestion des TV (ajout, recherche, nom, TV par défaut, IP), appairage et certificat client.
/// Les commandes passent par <see cref="Core.ViewModels.RemoteViewModel.Run"/> (barre d'état et
/// journal communs) ; leur sortie complète s'affiche dans « Résultat ».
/// </summary>
public partial class TvWindow : Window
{
    private readonly AppServices _app;

    public TvWindow(AppServices app)
    {
        InitializeComponent();
        _app = app;
        DbPathText.Text = "Base de l'app (TV, jetons chiffrés, réglages, journal) : " + app.Db.Path;
        ShowCertificate();
        app.Remote.PropertyChanged += OnRemoteChanged;
        Closed += (_, _) => app.Remote.PropertyChanged -= OnRemoteChanged;
        ShowActiveTv();
    }

    private string? Alias => _app.Remote.CurrentTv;
    private TvController Tv => _app.Tv;

    private void OnRemoteChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_app.Remote.CurrentTv)) ShowActiveTv();
    }

    private void ShowActiveTv()
    {
        ActiveTv.Text = Alias ?? "Aucune TV — recherchez puis ajoutez-en une";
        RenameBox.Text = Alias ?? "";
    }

    /// <summary>Exécute via le ViewModel (barre d'état, journal) et affiche la sortie complète.</summary>
    private async Task<CommandResult> Run(Func<Task<CommandResult>> action)
    {
        var r = await _app.Remote.Run(action);
        Output.Text = $"$ {r.CommandLine}\r\n[code {(r.TimedOut ? "timeout" : r.ExitCode.ToString())}]\r\n" +
                      r.StandardOutput + (r.StandardError.Length > 0 ? "\r\n" + r.StandardError : "");
        return r;
    }

    private void Say(string text, bool failed = false)
    {
        _app.Remote.Report(text, failed);
        Output.AppendText((Output.Text.Length > 0 ? "\r\n\r\n" : "") + text);
    }

    // ------------------------------------------------------------- TV ---------------------

    private void OnShowTvs(object s, RoutedEventArgs e)
    {
        _app.Remote.ReloadTvs();
        var tvs = Tv.TvStore.Load();
        Output.Text = $"Enregistrées dans {Tv.TvStore.Location}\r\n\r\n" +
                      (tvs.Count == 0 ? "Aucune TV configurée." : string.Join("\r\n", tvs));
    }

    private async void OnSetDefault(object s, RoutedEventArgs e)
    {
        if (Alias is { } a) await Run(() => Tv.ConfigSetDefaultAsync(a));
    }

    private async void OnStatus(object s, RoutedEventArgs e) => await Run(() => Tv.StatusAsync(Alias));

    private async void OnInfo(object s, RoutedEventArgs e) => await Run(() => Tv.RunForTvAsync(Alias, "info"));

    private async void OnRename(object s, RoutedEventArgs e)
    {
        var newName = RenameBox.Text.Trim();
        if (Alias is not { } old || newName.Length == 0 || newName == old) return;
        var r = await Run(() => Tv.ConfigSetAliasAsync(old, newName));
        if (!r.Success) return;
        _app.Remote.CurrentTv = newName;
        _app.Remote.ReloadTvs();
    }

    // ------------------------------------------------------------- réseau -----------------

    private async void OnDiscover(object s, RoutedEventArgs e)
    {
        _app.Remote.Report("Recherche des TV sur le réseau (~10 s)…");
        await Run(() => Tv.DiscoverAsync());
        var known = Tv.TvStore.Load().Select(t => t.Host).ToHashSet();
        var found = Tv.Engine.LastDiscovered.Select(d => d.Ip).ToList();
        if (found.FirstOrDefault(ip => !known.Contains(ip)) is { } fresh) NewIp.Text = fresh;
        if (found.Count > 0)
            Output.AppendText($"\r\nDéjà enregistrées : {string.Join(", ", found.Where(known.Contains).DefaultIfEmpty("aucune"))}");
    }

    /// <summary>
    /// La TV a changé d'IP (bail DHCP) : on la retrouve par sa MAC Ethernet ou Wi-Fi (descripteur UPnP
    /// lu pendant la recherche, sinon table ARP), puis on corrige son adresse.
    /// </summary>
    private async void OnRelocate(object s, RoutedEventArgs e)
    {
        var entry = TvLookup.Find(Tv.TvStore.Load(), Alias);
        if (entry is null) { Say($"« {Alias} » n'est pas enregistrée.", failed: true); return; }
        if (entry.Mac is null) { Say($"MAC inconnue pour « {entry.Key} » (la rajouter TV allumée).", failed: true); return; }

        _app.Remote.Report("Recherche des TV sur le réseau (~10 s)…");
        await Run(() => Tv.DiscoverAsync());
        var wanted = new[] { entry.Mac, entry.MacWifi }.OfType<string>().Select(ArpTable.NormalizeMac).ToHashSet();
        var ip = Tv.Engine.LastDiscovered.FirstOrDefault(d =>
                     new[] { d.Mac, d.MacEthernet, d.MacWifi }.Any(m => m is not null && wanted.Contains(ArpTable.NormalizeMac(m))))?.Ip
                 ?? await ArpTable.FindIpAsync(entry.Mac);
        if (ip is null)
            Say($"TV introuvable sur le réseau (MAC {entry.Mac}) — est-elle allumée ?", failed: true);
        else if (ip == entry.Host)
            Say($"IP inchangée ({ip}) — si la TV ne répond pas, elle est sans doute éteinte.");
        else if (Tv.TvStore.UpdateHost(entry.Id, ip))
            Say($"Adresse de « {entry.Key} » mise à jour : {entry.Host} → {ip}\r\n" +
                "Conseil : réserver cette IP pour la TV dans le DHCP de la box.");
        else
            Say("Échec de la mise à jour de l'adresse.", failed: true);
    }

    private async void OnAddTv(object s, RoutedEventArgs e)
    {
        var ip = NewIp.Text.Trim();
        var alias = NewAlias.Text.Trim();
        if (ip.Length == 0 || alias.Length == 0) { Say("Adresse IP et nom requis.", failed: true); return; }
        var r = await Run(() => Tv.ConfigAddAsync(ip, alias));
        if (!r.Success) return;
        _app.Remote.ReloadTvs();
        _app.Remote.CurrentTv = alias;
    }

    // ------------------------------------------------------------- appairage --------------

    private void OnPair(object s, RoutedEventArgs e) =>
        new PairingDialog(Tv.StartPairing(Alias)) { Owner = this }.ShowDialog();

    private async void OnAuthStatus(object s, RoutedEventArgs e) => await Run(() => Tv.AuthStatusAsync(Alias));
    private async void OnAuthRefresh(object s, RoutedEventArgs e) => await Run(() => Tv.AuthRefreshAsync(Alias));

    private async void OnAuthClear(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Oublier l'appairage de « {Alias} » ? Il faudra refaire le code PIN.", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await Run(() => Tv.AuthClearAsync(Alias));
    }

    // ------------------------------------------------------------- diagnostic -------------

    private async void OnListen(object s, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)s;
        button.IsEnabled = false;
        _app.Remote.Report("Écoute de la TV pendant 60 s…");
        try { await Run(() => Tv.RunForTvAsync(Alias, "listen", "60")); }
        finally { button.IsEnabled = true; }
    }

    private async void OnRawSend(object s, RoutedEventArgs e) =>
        await Run(() => Tv.RunForTvAsync(Alias, "raw", RawTopic.Text.Trim(), RawPayload.Text));

    // ------------------------------------------------------------- certificat -------------

    private void ShowCertificate()
    {
        var found = ClientCertificate.Find(_app.Settings.CertDirectory);
        CertState.Text = found is { } c
            ? "✔ Installé : " + Path.GetDirectoryName(c.Cert)
            : "⚠ Absent : la TV refusera la connexion tant qu'il n'est pas importé.";
        CertState.Foreground = (System.Windows.Media.Brush)FindResource(found is null ? "DangerBrush" : "SuccessBrush");
    }

    private void OnImportP12(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Certificat client VIDAA (.p12)", Filter = "Conteneur PKCS#12|*.p12;*.pfx|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var subject = ClientCertificate.ImportPkcs12(dlg.FileName, P12Password.Password, _app.Settings.CertDirectory);
            P12Password.Clear();
            Say($"Certificat importé ({subject}).");
        }
        catch (CryptographicException ex)
        {
            Say("Import impossible (mot de passe incorrect ou fichier invalide) : " + ex.Message, failed: true);
        }
        ShowCertificate();
    }

    private void OnImportPem(object s, RoutedEventArgs e)
    {
        var cert = new OpenFileDialog { Title = "Certificat (vidaa_client.pem)", Filter = "Certificat PEM|*.pem;*.crt|Tous les fichiers|*.*" };
        if (cert.ShowDialog(this) != true) return;
        var key = new OpenFileDialog { Title = "Clé privée (vidaa_client.key)", Filter = "Clé PEM|*.key;*.pem|Tous les fichiers|*.*" };
        if (key.ShowDialog(this) != true) return;
        try { Say($"Certificat importé ({ClientCertificate.ImportPem(cert.FileName, key.FileName, _app.Settings.CertDirectory)})."); }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            Say("Import impossible : " + ex.Message, failed: true);
        }
        ShowCertificate();
    }

    private void OnOpenCertDir(object s, RoutedEventArgs e)
    {
        var dir = string.IsNullOrWhiteSpace(_app.Settings.CertDirectory) ? ClientCertificate.DefaultDirectory : _app.Settings.CertDirectory;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
}
