namespace HisenseTvRemote.Core;

/// <summary>Résultat d'une commande envoyée à la TV (journalisé tel quel dans l'UI).</summary>
public sealed record CommandResult(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    DateTime StartedAt,
    bool TimedOut = false,
    bool StartFailed = false)
{
    public bool Success => !TimedOut && !StartFailed && ExitCode == 0;

    /// <summary>Commande exacte, telle qu'exécutée (ex. <c>tv --tv salon key home</c>).</summary>
    public string CommandLine =>
        string.Join(' ', new[] { Quote(ExecutablePath) }.Concat(Arguments.Select(Quote)));

    /// <summary>
    /// Une ligne pour la barre d'état (le détail complet reste dans le journal).
    /// Échec : dernière ligne non vide de stderr (sinon stdout) — c'est en général le message
    /// utile (« TV 'x' not found… »). Succès : première ligne de stdout, suivie du nombre de
    /// lignes restantes pour une liste (« Available apps: (+30 lignes) »).
    /// </summary>
    public string Summary
    {
        get
        {
            if (TimedOut) return "délai dépassé";
            if (Success)
            {
                var lines = Lines(StandardOutput);
                return lines.Length switch
                {
                    0 => "",
                    1 => lines[0],
                    _ => $"{lines[0]} (+{lines.Length - 1} lignes)",
                };
            }
            return Lines(StandardError.Trim().Length > 0 ? StandardError : StandardOutput).LastOrDefault() ?? "";
        }
    }

    private static string[] Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Quote(string s) =>
        s.Length == 0 || s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;
}
