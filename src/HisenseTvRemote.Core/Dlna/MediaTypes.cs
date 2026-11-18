namespace HisenseTvRemote.Core.Dlna;

public enum MediaKind { Photo, Video, Audio }

/// <summary>Type d'un fichier partageable : type MIME annoncé à la TV et genre (photo, vidéo, musique).</summary>
public sealed record MediaType(string Mime, MediaKind Kind)
{
    /// <summary>
    /// Champ « protocolInfo » DLNA. Photos : transfert « interactif » ; vidéo et musique : « streaming »
    /// avec déplacement par octets (DLNA.ORG_OP=01), indispensable pour avancer/reculer.
    /// </summary>
    public string ProtocolInfo => Kind == MediaKind.Photo
        ? $"http-get:*:{Mime}:{(Mime == "image/jpeg" ? "DLNA.ORG_PN=JPEG_LRG;" : "")}DLNA.ORG_OP=00;DLNA.ORG_FLAGS=00f00000000000000000000000000000"
        : $"http-get:*:{Mime}:DLNA.ORG_OP=01;DLNA.ORG_FLAGS=01700000000000000000000000000000";

    /// <summary>Partie après « http-get:*:mime: », renvoyée dans l'en-tête contentFeatures.dlna.org.</summary>
    public string ContentFeatures => ProtocolInfo[(ProtocolInfo.IndexOf(Mime, StringComparison.Ordinal) + Mime.Length + 1)..];

    public string UpnpClass => Kind switch
    {
        MediaKind.Photo => "object.item.imageItem.photo",
        MediaKind.Video => "object.item.videoItem",
        _ => "object.item.audioItem.musicTrack",
    };
}

/// <summary>
/// Extensions acceptées, d'après les formats que la 55A53FXVT déclare (ConnectionManager, 17/11). Les photos
/// d'autres formats (HEIC des téléphones, WebP, TIFF) sont converties en JPEG avant l'envoi, par l'app.
/// </summary>
public static class MediaTypes
{
    private static readonly Dictionary<string, MediaType> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new("image/jpeg", MediaKind.Photo), [".jpeg"] = new("image/jpeg", MediaKind.Photo),
        [".png"] = new("image/png", MediaKind.Photo), [".gif"] = new("image/gif", MediaKind.Photo),
        [".bmp"] = new("image/x-ms-bmp", MediaKind.Photo),
        [".mp4"] = new("video/mp4", MediaKind.Video), [".m4v"] = new("video/m4v", MediaKind.Video),
        [".mkv"] = new("video/x-matroska", MediaKind.Video), [".webm"] = new("video/webm", MediaKind.Video),
        [".avi"] = new("video/avi", MediaKind.Video), [".mov"] = new("video/quicktime", MediaKind.Video),
        [".mpg"] = new("video/mpeg", MediaKind.Video), [".mpeg"] = new("video/mpeg", MediaKind.Video),
        [".ts"] = new("video/mp2ts", MediaKind.Video), [".m2ts"] = new("video/m2ts", MediaKind.Video),
        [".mts"] = new("video/mts", MediaKind.Video), [".wmv"] = new("video/x-ms-wmv", MediaKind.Video),
        [".3gp"] = new("video/3gpp", MediaKind.Video), [".flv"] = new("video/x-flv", MediaKind.Video),
        [".mp3"] = new("audio/mpeg", MediaKind.Audio), [".flac"] = new("audio/flac", MediaKind.Audio),
        [".aac"] = new("audio/aac", MediaKind.Audio), [".m4a"] = new("audio/mp4", MediaKind.Audio),
        [".ogg"] = new("audio/ogg", MediaKind.Audio), [".wav"] = new("audio/x-wav", MediaKind.Audio),
        [".wma"] = new("audio/x-ms-wma", MediaKind.Audio),
    };

    /// <summary>Photos lisibles seulement après conversion en JPEG.</summary>
    public static readonly IReadOnlySet<string> ConvertiblePhotos =
        new HashSet<string>([".heic", ".heif", ".webp", ".tif", ".tiff", ".jxr", ".wdp"], StringComparer.OrdinalIgnoreCase);

    public static MediaType? Of(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var t) ? t
        : ConvertiblePhotos.Contains(Path.GetExtension(path)) ? new MediaType("image/jpeg", MediaKind.Photo)
        : null;

    public static bool IsSupported(string path) => Of(path) is not null;

    /// <summary>Filtre de la boîte « Ouvrir » : toutes les extensions acceptées.</summary>
    public static string OpenFileFilter
    {
        get
        {
            static string Pattern(IEnumerable<string> exts) => string.Join(";", exts.Select(e => "*" + e));
            var photos = ByExtension.Where(p => p.Value.Kind == MediaKind.Photo).Select(p => p.Key).Concat(ConvertiblePhotos);
            var videos = ByExtension.Where(p => p.Value.Kind == MediaKind.Video).Select(p => p.Key);
            var audio = ByExtension.Where(p => p.Value.Kind == MediaKind.Audio).Select(p => p.Key);
            return $"Photos, vidéos et musique|{Pattern(photos.Concat(videos).Concat(audio))}" +
                   $"|Photos|{Pattern(photos)}|Vidéos|{Pattern(videos)}|Musique|{Pattern(audio)}";
        }
    }
}

/// <summary>Plage d'octets demandée par la TV (en-tête HTTP « Range: bytes=… »), bornes incluses.</summary>
public readonly record struct ByteRange(long Start, long End)
{
    public long Length => End - Start + 1;

    /// <summary>
    /// Interprète « bytes=a-b », « bytes=a- » ou « bytes=-n » pour un fichier de <paramref name="size"/> octets.
    /// Null : pas de plage (fichier entier). <see cref="Unsatisfiable"/> : plage hors du fichier (réponse 416).
    /// Une seule plage est gérée (c'est ce que demandent les lecteurs ; les plages multiples → fichier entier).
    /// </summary>
    public static ByteRange? Parse(string? header, long size)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        header = header.Trim();
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(',')) return null;
        var spec = header[6..].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0) return null;
        var a = spec[..dash].Trim();
        var b = spec[(dash + 1)..].Trim();
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var suffix) || suffix <= 0) return Unsatisfiable;
            return size == 0 ? Unsatisfiable : new ByteRange(Math.Max(0, size - suffix), size - 1);
        }
        if (!long.TryParse(a, out var start) || start < 0 || start >= size) return Unsatisfiable;
        if (b.Length == 0) return new ByteRange(start, size - 1);
        if (!long.TryParse(b, out var end) || end < start) return Unsatisfiable;
        return new ByteRange(start, Math.Min(end, size - 1));
    }

    public static readonly ByteRange Unsatisfiable = new(-1, -2);
    public bool IsUnsatisfiable => Start < 0;
}
