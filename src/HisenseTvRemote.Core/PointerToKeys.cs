namespace HisenseTvRemote.Core;

/// <summary>
/// Mode souris : traduit les déplacements de la souris du PC en touches directionnelles de la TV
/// (le protocole de déplacement du pointeur de la TV n'est pas connu, voir contexte/interface.md).
/// Une touche par <see cref="Threshold"/> pixels parcourus, dans l'axe dominant ; l'autre axe est remis
/// à zéro pour éviter les dérives en diagonale.
/// </summary>
public sealed class PointerToKeys(double threshold = 90)
{
    private double _x;
    private double _y;

    /// <summary>Distance (pixels physiques) pour un pas de navigation.</summary>
    public double Threshold { get; set; } = threshold;

    /// <summary>Ajoute un déplacement ; renvoie les touches à envoyer (souvent aucune).</summary>
    public IReadOnlyList<string> Add(double dx, double dy)
    {
        _x += dx;
        _y += dy;
        var keys = new List<string>();
        while (Math.Abs(_x) >= Threshold || Math.Abs(_y) >= Threshold)
        {
            if (Math.Abs(_x) >= Math.Abs(_y))
            {
                keys.Add(_x > 0 ? "right" : "left");
                _x -= Math.Sign(_x) * Threshold;
                _y = 0;
            }
            else
            {
                keys.Add(_y > 0 ? "down" : "up");
                _y -= Math.Sign(_y) * Threshold;
                _x = 0;
            }
        }
        return keys;
    }

    public void Reset() => _x = _y = 0;

    public const int MinThreshold = 30;
    public const int MaxThreshold = 300;

    /// <summary>Sensibilité ajustée d'un cran (± 10 px), bornée ; renvoie la nouvelle valeur.</summary>
    public int Adjust(bool moreSensitive)
    {
        Threshold = Math.Clamp(Threshold + (moreSensitive ? -10 : 10), MinThreshold, MaxThreshold);
        Reset();
        return (int)Threshold;
    }
}
