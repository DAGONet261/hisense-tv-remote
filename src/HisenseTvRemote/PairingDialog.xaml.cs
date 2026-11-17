using System.ComponentModel;
using System.Windows;
using HisenseTvRemote.Core;

namespace HisenseTvRemote;

public partial class PairingDialog : Window
{
    private readonly IPairingSession _session;
    private bool _closingAfterCancel;

    public bool Paired { get; private set; }

    public PairingDialog(IPairingSession session)
    {
        InitializeComponent();
        _session = session;
        _session.OutputReceived += chunk => Dispatcher.BeginInvoke(() =>
        {
            Console.AppendText(chunk);
            Console.ScrollToEnd();
        });
        // Le PIN n'est affiché par la TV qu'une fois connecté : on n'active la saisie qu'au prompt.
        _session.PinRequested += () => Dispatcher.BeginInvoke(() =>
        {
            if (_session.Result.IsCompleted) return;
            Hint.Text = "Le code PIN est affiché sur l'écran de la TV. Saisissez-le puis validez (≈ 60 s).";
            PinBox.IsEnabled = SendBtn.IsEnabled = true;
            PinBox.Focus();
        });
        _session.Completed += r => Dispatcher.BeginInvoke(() =>
        {
            Paired = r.Success;
            PinBox.IsEnabled = SendBtn.IsEnabled = false;
            Console.AppendText(r.Success
                ? "\r\n✔ Appairage terminé."
                : $"\r\n✘ Appairage échoué ou annulé (code {r.ExitCode}).");
            if (r.StandardError.Trim().Length > 0) Console.AppendText("\r\n" + r.StandardError.Trim());
            Console.ScrollToEnd();
            CloseBtn.Content = "Fermer";
        });
        Loaded += (_, _) => _session.Start();
        Closing += OnClosing;
        Closed += (_, _) => _session.Dispose();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var pin = PinBox.Text.Trim();
        if (pin.Length == 0) return;
        PinBox.Clear();
        PinBox.IsEnabled = SendBtn.IsEnabled = false; // un seul essai par session : la TV vérifie puis la session se termine
        Console.AppendText($"\r\n> {pin}\r\n");
        try { await _session.SubmitPinAsync(pin); }
        catch (System.IO.IOException) { /* le processus s'est terminé entre-temps */ }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Fermeture pendant l'appairage : annulation propre de la session avant de fermer.</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterCancel || _session.Result.IsCompleted) return;
        e.Cancel = true;
        _closingAfterCancel = true;
        CloseBtn.IsEnabled = PinBox.IsEnabled = SendBtn.IsEnabled = false;
        Console.AppendText("\r\nAnnulation…");
        await _session.CancelAsync();
        Close();
    }
}
