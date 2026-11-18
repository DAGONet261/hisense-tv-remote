using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HisenseTvRemote.Core.Dlna;

namespace HisenseTvRemote;

/// <summary>
/// Prépare une photo avant l'envoi à la TV, sans toucher à l'original :
/// <list type="bullet">
/// <item>convertit en JPEG les formats que la TV ne lit pas (HEIC des téléphones, WebP, TIFF) ;</item>
/// <item>redresse les photos selon leur orientation EXIF (la TV l'ignore : photos de téléphone couchées) ;</item>
/// <item>réduit à la taille d'un écran 4K (3840 × 2160) les très grandes images, pour un affichage rapide.</item>
/// </list>
/// Les copies vont dans un dossier temporaire, effacé à la fermeture de l'écran de partage. Le décodage
/// passe par les codecs de Windows (WIC) : le HEIC demande les « Extensions d'image HEIF » du Microsoft Store.
/// </summary>
public sealed class PhotoPreparer : IDisposable
{
    private const int MaxWidth = 3840;
    private const int MaxHeight = 2160;
    private const long MaxBytesAsIs = 20L * 1024 * 1024;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HisenseTvRemote", "partage", Guid.NewGuid().ToString("N"));

    public Task<string> PrepareAsync(string path, CancellationToken ct) => Task.Run(() => Prepare(path), ct);

    private string Prepare(string path)
    {
        var ext = Path.GetExtension(path);
        var mustConvert = MediaTypes.ConvertiblePhotos.Contains(ext);
        BitmapFrame frame;
        int orientation;
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
            orientation = Orientation(frame);              // métadonnées lues tant que le fichier est ouvert
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new NotSupportedException(ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase)
                ? "photo HEIC illisible : installez « Extensions d'image HEIF » depuis le Microsoft Store."
                : $"image illisible ({ex.Message})");
        }

        var swap = orientation is 5 or 6 or 7 or 8;                     // rotation d'un quart de tour
        var w = swap ? frame.PixelHeight : frame.PixelWidth;
        var h = swap ? frame.PixelWidth : frame.PixelHeight;
        var scale = Math.Min(1.0, Math.Min((double)MaxWidth / w, (double)MaxHeight / h));
        var big = new FileInfo(path).Length > MaxBytesAsIs;
        if (!mustConvert && orientation == 1 && scale >= 1.0 && !big) return path;     // envoyée telle quelle

        var (angle, flip) = orientation switch
        {
            2 => (0, true), 3 => (180, false), 4 => (180, true), 5 => (90, true),
            6 => (90, false), 7 => (270, true), 8 => (270, false), _ => (0, false),
        };
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(flip ? -scale : scale, scale));
        if (angle != 0) transform.Children.Add(new RotateTransform(angle));
        BitmapSource result = new TransformedBitmap(frame, transform);
        if (result.Format != PixelFormats.Bgr24 && result.Format != PixelFormats.Gray8)
            result = new FormatConvertedBitmap(result, PixelFormats.Bgr24, null, 0);

        Directory.CreateDirectory(_dir);
        var target = Path.Combine(_dir, Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N")[..8] + ".jpg");
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(result));
        using (var output = File.Create(target)) encoder.Save(output);
        return target;
    }

    /// <summary>Orientation EXIF (1 à 8 ; 1 = normale), lue dans les emplacements JPEG puis HEIC/TIFF.</summary>
    private static int Orientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata meta) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (meta.GetQuery(query) is ushort o && o is >= 1 and <= 8) return o;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) { }
        }
        return 1;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
