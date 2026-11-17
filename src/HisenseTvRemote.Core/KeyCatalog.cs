namespace HisenseTvRemote.Core;

/// <summary>Touches de la télécommande (SPEC.md §3.1) et applications (§3.2).</summary>
public static class KeyCatalog
{
    public static readonly IReadOnlyList<string> Power = ["power"];
    public static readonly IReadOnlyList<string> Navigation = ["up", "down", "left", "right", "ok"];
    // Rembobiner = « backs » (KEY_BACKS), pendant de « forwards » : vérifié le 17/11 dans le lecteur USB et
    // YouTube. KEY_BACK (« back », le rembobinage selon pyvidaa) n'y fait rien : rangé dans les autres touches.
    // Le retour arrière est « returns » (KEY_RETURNS).
    public static readonly IReadOnlyList<string> Menu = ["menu", "home", "returns", "exit"];
    public static readonly IReadOnlyList<string> Volume = ["volumeup", "volumedown", "mute"];
    public static readonly IReadOnlyList<string> Playback = ["play", "pause", "stop", "backs"];
    public static readonly IReadOnlyList<string> Digits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];
    public static readonly IReadOnlyList<string> Channels = ["channelup", "channeldown"];
    public static readonly IReadOnlyList<string> Colors = ["red", "green", "yellow", "blue"];
    public static readonly IReadOnlyList<string> Other =
    [
        "back", "channeldot", "forwards", "info", "leftmousekeys", "mute_long_press", "ok_long_press",
        "subtitle", "uddleftmousekeys", "uduleftmousekeys", "voicedown", "voiceup", "zoomin", "zoomout"
    ];

    public static IReadOnlyList<string> All { get; } =
        [.. Power, .. Navigation, .. Menu, .. Volume, .. Playback, .. Digits, .. Channels, .. Colors, .. Other];

    /// <summary>Nom français affiché sur chaque bouton (le nom technique reste en info-bulle).</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["power"] = "Marche / Arrêt",
        ["up"] = "Haut", ["down"] = "Bas", ["left"] = "Gauche", ["right"] = "Droite", ["ok"] = "OK",
        ["menu"] = "Menu", ["home"] = "Accueil", ["returns"] = "Retour", ["exit"] = "Quitter",
        ["volumeup"] = "Volume +", ["volumedown"] = "Volume −", ["mute"] = "Muet",
        ["play"] = "Lecture", ["pause"] = "Pause", ["stop"] = "Stop",
        ["backs"] = "Rembobiner", ["forwards"] = "Avance rapide",
        ["channelup"] = "Chaîne +", ["channeldown"] = "Chaîne −",
        ["red"] = "Rouge", ["green"] = "Vert", ["yellow"] = "Jaune", ["blue"] = "Bleu",
        ["channeldot"] = "Point (chaîne)", ["info"] = "Infos", ["subtitle"] = "Sous-titres",
        ["zoomin"] = "Zoom +", ["zoomout"] = "Zoom −",
        ["mute_long_press"] = "Muet (appui long)", ["ok_long_press"] = "OK (appui long)",
        // Touches peu documentées : libellés d'après leur nom, effet à confirmer devant la TV.
        ["leftmousekeys"] = "Mode souris", ["uddleftmousekeys"] = "Souris : appui", ["uduleftmousekeys"] = "Souris : relâche",
        ["voiceup"] = "Voix : relâche", ["voicedown"] = "Voix : appui",
        ["back"] = "Back (sans effet constaté)",
    };

    public static string Label(string key) => Labels.TryGetValue(key, out var l) ? l : key;

    public static readonly IReadOnlyList<string> Apps = ["netflix", "youtube", "amazon", "disney", "hulu"];

    /// <summary>Noms courts acceptés par <c>tv source</c> ; les autres entrées viennent de <c>source list</c>.</summary>
    public static readonly IReadOnlyList<string> Sources = ["tv", "hdmi1", "hdmi2", "av"];
}
