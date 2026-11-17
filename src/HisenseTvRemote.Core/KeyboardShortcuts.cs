namespace HisenseTvRemote.Core;

/// <summary>Modificateurs d'un raccourci (indépendant de WPF pour rester testable).</summary>
[Flags]
public enum KeyMods
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>Ce que fait un raccourci.</summary>
public enum ShortcutCommand
{
    /// <summary>Envoie la touche TV <see cref="Shortcut.TvKey"/>.</summary>
    TvKey,
    VolumeUp,
    VolumeDown,
    Mute,
    /// <summary>Lecture et pause en alternance (le clavier n'a qu'une touche pour les deux).</summary>
    PlayPause,
    RefreshLists,
    OpenJournal,
    OpenSettings,
    OpenMouseMode,
    ShowHelp,
}

/// <param name="Display">Touches telles qu'affichées à l'utilisateur (« Ctrl + ↑ »).</param>
/// <param name="Label">Action en français (« Volume + »).</param>
/// <param name="Group">Rubrique de l'aide (« Navigation », « Volume »…).</param>
public sealed record Shortcut(string Display, string Label, string Group, ShortcutCommand Command, string? TvKey = null)
{
    /// <summary>Noms de touches (enum WPF <c>Key</c>) qui déclenchent ce raccourci.</summary>
    internal IReadOnlyList<string> Keys { get; init; } = [];
    /// <summary>Ou bien : caractère produit par la touche (sans Maj) selon la disposition du clavier.</summary>
    internal IReadOnlyList<char> Chars { get; init; } = [];
    internal KeyMods Mods { get; init; }
}

/// <summary>
/// Raccourcis clavier de la fenêtre principale. Les lettres, la rangée des chiffres et les symboles ne sont
/// pas des raccourcis : ils démarrent la saisie de texte (champ de recherche de la TV). Le pavé numérique
/// envoie les chiffres de la télécommande. Pas de raccourci pour Marche/Arrêt (trop facile à déclencher
/// par erreur).
/// </summary>
public static class KeyboardShortcuts
{
    private static Shortcut K(string display, string tvKey, string group, KeyMods mods, params string[] keys) =>
        new(display, KeyCatalog.Label(tvKey), group, ShortcutCommand.TvKey, tvKey) { Keys = keys, Mods = mods };

    private static Shortcut C(string display, string label, string group, ShortcutCommand cmd, KeyMods mods, params string[] keys) =>
        new(display, label, group, cmd) { Keys = keys, Mods = mods };

    public static IReadOnlyList<Shortcut> All { get; } =
    [
        K("↑", "up", "Navigation", KeyMods.None, "Up"),
        K("↓", "down", "Navigation", KeyMods.None, "Down"),
        K("←", "left", "Navigation", KeyMods.None, "Left"),
        K("→", "right", "Navigation", KeyMods.None, "Right"),
        K("Entrée", "ok", "Navigation", KeyMods.None, "Return", "Enter"),
        K("Échap  ou  Retour arrière", "returns", "Navigation", KeyMods.None, "Escape", "Back"),
        K("Début (↖)", "home", "Navigation", KeyMods.None, "Home"),
        K("Fin", "exit", "Navigation", KeyMods.None, "End"),
        K("F2", "menu", "Navigation", KeyMods.None, "F2"),

        C("Ctrl + ↑  ou  + (pavé num.)", "Volume +", "Volume", ShortcutCommand.VolumeUp, KeyMods.Ctrl, "Up"),
        C("Ctrl + ↓  ou  − (pavé num.)", "Volume −", "Volume", ShortcutCommand.VolumeDown, KeyMods.Ctrl, "Down"),
        C("+ (pavé num.)", "Volume +", "Volume", ShortcutCommand.VolumeUp, KeyMods.None, "Add"),
        C("− (pavé num.)", "Volume −", "Volume", ShortcutCommand.VolumeDown, KeyMods.None, "Subtract"),
        C("Ctrl + M", "Muet", "Volume", ShortcutCommand.Mute, KeyMods.Ctrl, "M"),
        C("Touche muet du clavier", "Muet", "Volume", ShortcutCommand.Mute, KeyMods.None, "VolumeMute"),

        K("Page préc.", "channelup", "Chaînes", KeyMods.None, "PageUp", "Prior"),
        K("Page suiv.", "channeldown", "Chaînes", KeyMods.None, "PageDown", "Next"),

        C("Espace", "Lecture / Pause", "Lecture", ShortcutCommand.PlayPause, KeyMods.None, "Space"),
        C("Lecture/Pause (clavier multimédia)", "Lecture / Pause", "Lecture", ShortcutCommand.PlayPause, KeyMods.None, "MediaPlayPause"),
        K("Stop (clavier multimédia)", "stop", "Lecture", KeyMods.None, "MediaStop"),
        K("Piste suivante (clavier multimédia)", "forwards", "Lecture", KeyMods.None, "MediaNextTrack"),
        K("Piste précédente (clavier multimédia)", "back", "Lecture", KeyMods.None, "MediaPreviousTrack"),

        new("Ctrl + « + »", KeyCatalog.Label("zoomin"), "Affichage", ShortcutCommand.TvKey, "zoomin")
            { Keys = ["Add", "OemPlus"], Chars = ['+', '='], Mods = KeyMods.Ctrl },
        new("Ctrl + « − »", KeyCatalog.Label("zoomout"), "Affichage", ShortcutCommand.TvKey, "zoomout")
            { Keys = ["Subtract", "OemMinus"], Chars = ['-'], Mods = KeyMods.Ctrl },
        K("F3", "info", "Affichage", KeyMods.None, "F3"),
        K("F4", "subtitle", "Affichage", KeyMods.None, "F4"),

        K("0 (pavé num.)", "0", "Chiffres", KeyMods.None, "NumPad0"),
        K("1 (pavé num.)", "1", "Chiffres", KeyMods.None, "NumPad1"),
        K("2 (pavé num.)", "2", "Chiffres", KeyMods.None, "NumPad2"),
        K("3 (pavé num.)", "3", "Chiffres", KeyMods.None, "NumPad3"),
        K("4 (pavé num.)", "4", "Chiffres", KeyMods.None, "NumPad4"),
        K("5 (pavé num.)", "5", "Chiffres", KeyMods.None, "NumPad5"),
        K("6 (pavé num.)", "6", "Chiffres", KeyMods.None, "NumPad6"),
        K("7 (pavé num.)", "7", "Chiffres", KeyMods.None, "NumPad7"),
        K("8 (pavé num.)", "8", "Chiffres", KeyMods.None, "NumPad8"),
        K("9 (pavé num.)", "9", "Chiffres", KeyMods.None, "NumPad9"),

        K("Ctrl + 1", "red", "Touches de couleur", KeyMods.Ctrl, "D1", "NumPad1"),
        K("Ctrl + 2", "green", "Touches de couleur", KeyMods.Ctrl, "D2", "NumPad2"),
        K("Ctrl + 3", "yellow", "Touches de couleur", KeyMods.Ctrl, "D3", "NumPad3"),
        K("Ctrl + 4", "blue", "Touches de couleur", KeyMods.Ctrl, "D4", "NumPad4"),

        C("F5", "Actualiser apps et sources", "Application", ShortcutCommand.RefreshLists, KeyMods.None, "F5"),
        C("Ctrl + J", "Ouvrir le journal", "Application", ShortcutCommand.OpenJournal, KeyMods.Ctrl, "J"),
        C("Ctrl + T", "Ouvrir TV et réglages", "Application", ShortcutCommand.OpenSettings, KeyMods.Ctrl, "T"),
        C("F11", "Mode souris (plein écran)", "Application", ShortcutCommand.OpenMouseMode, KeyMods.None, "F11"),
        C("F1", "Aide des raccourcis", "Application", ShortcutCommand.ShowHelp, KeyMods.None, "F1"),
    ];

    /// <summary>
    /// Raccourci correspondant à la touche pressée, ou null.
    /// </summary>
    /// <param name="key">Nom de la touche (enum WPF <c>Key</c>).</param>
    /// <param name="ch">Caractère produit par la touche sans Maj, selon la disposition du clavier (AZERTY :
    /// la touche 6 donne « - »). Sert aux raccourcis définis par caractère (zoom).</param>
    /// <param name="mods">Modificateurs ; Maj est ignorée pour les raccourcis définis par caractère
    /// (« + » se tape souvent avec Maj).</param>
    public static Shortcut? Match(string key, char? ch, KeyMods mods)
    {
        foreach (var s in All)
        {
            var byKey = s.Keys.Contains(key) && mods == s.Mods;
            var byChar = ch is { } c && s.Chars.Contains(c) && (mods & ~KeyMods.Shift) == s.Mods;
            if (byKey || byChar) return s;
        }
        return null;
    }

    /// <summary>Lignes de l'aide (une par action, sans doublon d'affichage).</summary>
    public static IEnumerable<IGrouping<string, Shortcut>> ForHelp() =>
        All.Where(s => !(s.Command is ShortcutCommand.VolumeUp or ShortcutCommand.VolumeDown && s.Mods == KeyMods.None))
           .GroupBy(s => s.Group);
}
