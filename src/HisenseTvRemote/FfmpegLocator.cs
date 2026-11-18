using System.IO;

namespace HisenseTvRemote;

/// <summary>
/// FFmpeg livré avec l'application (dossier <c>ffmpeg\</c> à côté de l'exe, copié par la compilation et
/// l'installeur), sinon celui du PATH. Sans FFmpeg, les TS/AVI partent tels quels (souvent refusés).
/// </summary>
public static class FfmpegLocator
{
    public static (string Ffmpeg, string Ffprobe)? Find()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (Pair(bundled) is { } b) return b;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { if (Pair(dir.Trim('"')) is { } p) return p; }
            catch (ArgumentException) { }                              // entrée de PATH invalide
        }
        return null;
    }

    private static (string, string)? Pair(string dir)
    {
        var ffmpeg = Path.Combine(dir, "ffmpeg.exe");
        var ffprobe = Path.Combine(dir, "ffprobe.exe");
        return File.Exists(ffmpeg) && File.Exists(ffprobe) ? (ffmpeg, ffprobe) : null;
    }
}
