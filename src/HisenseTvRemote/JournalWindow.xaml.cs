using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace HisenseTvRemote;

/// <summary>Journal : chaque commande envoyée (exacte), son code de sortie et ses sorties.</summary>
public partial class JournalWindow : Window
{
    private readonly AppServices _app;

    public JournalWindow(AppServices app)
    {
        InitializeComponent();
        _app = app;
        List.ItemsSource = app.Journal;
        DbPath.Text = app.Db is null ? "Historique non conservé (base indisponible)" : "Conservé dans " + app.Db.Path;
        // Suit les nouvelles commandes.
        app.Journal.CollectionChanged += OnJournalChanged;
        Closed += (_, _) => app.Journal.CollectionChanged -= OnJournalChanged;
        Loaded += (_, _) => ScrollToEnd();
    }

    private void OnJournalChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (List.Items.Count > 0) List.ScrollIntoView(List.Items[^1]);
    }

    private void OnSelected(object s, SelectionChangedEventArgs e) =>
        Detail.Text = (List.SelectedItem as LogEntry)?.Detail ?? "";

    private void OnCopy(object s, RoutedEventArgs e)
    {
        if (List.SelectedItem is LogEntry le) Clipboard.SetText(le.Detail);
    }

    private void OnClear(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Vider le journal (y compris l'historique conservé en base) ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _app.Journal.Clear();
        Detail.Clear();
        _app.Db?.ClearJournal();
    }
}
