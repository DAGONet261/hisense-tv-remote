using System.Collections.ObjectModel;
using System.IO;
using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Storage;
using HisenseTvRemote.Core.ViewModels;

namespace HisenseTvRemote;

/// <summary>
/// Ce que partagent les fenêtres : base, réglages, contrôleur de TV, ViewModel principal et journal.
/// Créé une fois au démarrage par <see cref="MainWindow"/>.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>Lignes de journal gardées à l'écran (la base en garde davantage).</summary>
    private const int VisibleJournal = 500;

    private AppServices(AppDatabase db)
    {
        Db = db;
        Settings = Settings.Load(db);
        Tv = TvController.ForDatabase(db, Settings);
        var iconDir = Path.Combine(Path.GetDirectoryName(db.Path)!, "icons");
        Remote = new RemoteViewModel(Tv, new IconCache(iconDir), Settings.CurrentAlias, alias =>
        {
            if (alias is null || alias == Settings.CurrentAlias) return;
            Settings.CurrentAlias = alias;
            Settings.Save();
        });
        foreach (var r in db.LoadJournal(200)) Journal.Add(new LogEntry(r));
    }

    public AppDatabase Db { get; }
    public Settings Settings { get; }
    public TvController Tv { get; }
    public RemoteViewModel Remote { get; }
    public ObservableCollection<LogEntry> Journal { get; } = [];

    /// <summary>Ouvre la base (à côté de l'exe si possible, sinon %APPDATA%). Lève en cas d'échec.</summary>
    public static AppServices Create() => new(new AppDatabase(AppDatabase.DefaultPath()));

    /// <summary>Ajoute au journal (écran + base). À appeler sur le thread UI.</summary>
    public void Record(CommandResult r)
    {
        Journal.Add(new LogEntry(r));
        while (Journal.Count > VisibleJournal) Journal.RemoveAt(0);
        try { Db.AddJournal(r); }
        catch (Microsoft.Data.Sqlite.SqliteException ex) { Remote.Report("Journal non enregistré : " + ex.Message, failed: true); }
    }

    public void Dispose() => Tv.Dispose();
}
