using System.Security.Cryptography;
using System.Text;

namespace HisenseTvRemote.Core.Storage;

/// <summary>
/// Logos officiels des apps, fournis par la TV (champ <c>httpIcon</c> de la liste d'apps :
/// <c>data:image/png;base64,iconDownloadhttps://img.vidaahub.com/…png</c> — parfois .jpg malgré le préfixe).
/// Téléchargés une seule fois,
/// gardés dans un dossier <c>icons</c> à côté de la base.
/// </summary>
public sealed class IconCache(string folder)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string Folder { get; } = folder;

    /// <summary>L'URL réelle contenue dans <c>httpIcon</c> (null si absente, comme pour les sources).</summary>
    public static string? ExtractUrl(string? httpIcon)
    {
        if (string.IsNullOrWhiteSpace(httpIcon)) return null;
        var i = httpIcon.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var url = httpIcon[i..].Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? url : null;
    }

    public string PathFor(string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")) ext = ".img";
        return Path.Combine(Folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant() + ext);
    }

    /// <summary>PNG, JPEG, GIF ou BMP (formats que WPF affiche sans codec supplémentaire).</summary>
    internal static bool IsImage(byte[] b) =>
        b.Length >= 4 && (b[0] == 0x89 && b[1] == (byte)'P'                     // PNG
                          || b[0] == 0xFF && b[1] == 0xD8                        // JPEG
                          || b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F'
                          || b[0] == (byte)'B' && b[1] == (byte)'M');

    /// <summary>Chemin local du logo (téléchargé au besoin) ; null si pas d'URL ou téléchargement impossible.</summary>
    public async Task<string?> GetAsync(string? httpIcon, CancellationToken ct = default)
    {
        if (ExtractUrl(httpIcon) is not { } url) return null;
        var path = PathFor(url);
        if (File.Exists(path)) return path;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (!IsImage(bytes)) return null; // pas une image (page d'erreur…)
            Directory.CreateDirectory(Folder);
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
