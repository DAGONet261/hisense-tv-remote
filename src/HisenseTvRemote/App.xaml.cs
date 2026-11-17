using System.IO;
using System.Windows;
using System.Windows.Threading;
using HisenseTvRemote.Core.Storage;

namespace HisenseTvRemote;

/// <summary>
/// Application. Filet de sécurité : une erreur inattendue de l'interface est affichée et consignée dans
/// <c>erreurs.log</c> (à côté de la base) au lieu de fermer l'application sans explication.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var log = Path.Combine(Path.GetDirectoryName(AppDatabase.DefaultPath())!, "erreurs.log");
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        MessageBox.Show($"Une erreur inattendue s'est produite ; l'application continue.\n\n{e.Exception.Message}",
            "VIDAA - Hisense", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
