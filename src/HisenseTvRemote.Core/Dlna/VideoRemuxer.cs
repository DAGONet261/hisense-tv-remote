using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>Pistes d'une vidéo, d'après ffprobe.</summary>
public sealed record VideoProbe(string? VideoCodec, string? AudioCodec);

/// <summary>
/// Change l'enveloppe des vidéos que la TV refuse (TS, MKV, AVI, WMV, FLV…) en MP4, avant l'envoi, avec
/// FFmpeg : image recopiée telle quelle (pas de perte), son recopié ou converti en AAC.
/// <para>
/// Pourquoi (mesuré sur la 55A53FXVT, 18/11) : un TS (H.264 864×488, pixels non carrés) et un MKV (VP9 4K
/// + Opus) étaient refusés (« fichier non pris en charge ») alors que le même contenu en MP4 se lit
/// normalement, VP9 compris : c'est l'enveloppe qui gêne son lecteur DLNA, pas les codecs.
/// </para>
/// Les MP4/MOV ne passent pas par ici (réorganisés à la volée par <see cref="Mp4Interleaver"/>). Les
/// conversions sont gardées (une par fichier) jusqu'à <see cref="Dispose"/>, qui efface le dossier temporaire.
/// </summary>
public sealed class VideoRemuxer(string ffmpegPath, string ffprobePath, string? tempDirectory = null) : IDisposable
{
    /// <summary>Codecs d'image que la TV lit en MP4 (VP9 vérifié en 4K) : recopiés tels quels.</summary>
    public static readonly IReadOnlySet<string> CopyVideo =
        new HashSet<string>(["h264", "hevc", "vp9", "mpeg4", "mpeg2video"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Codecs de son recopiés ; les autres (Opus, Vorbis, DTS, FLAC, PCM…) sont convertis en AAC.</summary>
    public static readonly IReadOnlySet<string> CopyAudio =
        new HashSet<string>(["aac", "mp3", "ac3", "eac3"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> NativeContainers = new([".mp4", ".m4v", ".mov"], StringComparer.OrdinalIgnoreCase);

    private readonly string _dir = tempDirectory ?? Path.Combine(Path.GetTempPath(), "HisenseTvRemote", "partage", "videos-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _done = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vidéo à convertir avant l'envoi (tout sauf MP4/M4V/MOV).</summary>
    public static bool NeedsRemux(string path) =>
        MediaTypes.Of(path)?.Kind == MediaKind.Video && !NativeContainers.Contains(Path.GetExtension(path));

    /// <summary>
    /// Convertit <paramref name="path"/> en MP4 (ou renvoie la conversion déjà faite). Progression de 0 à 1
    /// si la durée est connue. Lève <see cref="NotSupportedException"/> si l'image doit être réencodée.
    /// </summary>
    public async Task<string> RemuxAsync(string path, TimeSpan? duration, IProgress<double>? progress, CancellationToken ct)
    {
        if (_done.TryGetValue(path, out var ready) && File.Exists(ready)) return ready;

        var probe = ParseProbe(await RunAsync(ffprobePath,
            ["-v", "error", "-show_entries", "stream=index,codec_type,codec_name", "-of", "compact=p=0", path], null, ct).ConfigureAwait(false));
        var args = BuildArguments(path, Path.Combine(_dir, "x.mp4"), probe);      // vérifie les codecs avant tout
        Directory.CreateDirectory(_dir);
        var output = Path.Combine(_dir, Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N")[..8] + ".mp4");
        args[^1] = output;
        try
        {
            await RunAsync(ffmpegPath, args, line =>
            {
                if (progress is not null && duration is { } d && d > TimeSpan.Zero && ParseProgress(line) is { } t)
                    progress.Report(Math.Clamp(t / d, 0, 1));
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        _done[path] = output;
        return output;
    }

    /// <summary>Lit la sortie « compact » de ffprobe : première piste vidéo et première piste son.</summary>
    public static VideoProbe ParseProbe(string output)
    {
        string? video = null, audio = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('|').Select(f => f.Split('=', 2)).Where(kv => kv.Length == 2)
                .ToDictionary(kv => kv[0], kv => kv[1], StringComparer.Ordinal);
            if (!fields.TryGetValue("codec_type", out var type) || !fields.TryGetValue("codec_name", out var codec)) continue;
            if (type == "video" && video is null && codec is not ("mjpeg" or "png" or "bmp")) video = codec;   // pas les pochettes
            else if (type == "audio" && audio is null) audio = codec;
        }
        return new VideoProbe(video, audio);
    }

    /// <summary>Arguments FFmpeg : image recopiée ; son recopié ou converti en AAC ; sous-titres et données ignorés.</summary>
    public static List<string> BuildArguments(string input, string output, VideoProbe probe)
    {
        if (probe.VideoCodec is null) throw new NotSupportedException("aucune piste vidéo trouvée.");
        if (!CopyVideo.Contains(probe.VideoCodec))
            throw new NotSupportedException($"format d'image « {probe.VideoCodec} » non lu par la TV (réencodage non disponible pour l'instant).");
        var args = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-c:v", "copy" };
        if (probe.VideoCodec.Equals("hevc", StringComparison.OrdinalIgnoreCase)) args.AddRange(["-tag:v", "hvc1"]);
        if (probe.AudioCodec is null || CopyAudio.Contains(probe.AudioCodec)) args.AddRange(["-c:a", "copy"]);
        else args.AddRange(["-c:a", "aac", "-b:a", "192k", "-ac", "2"]);
        args.AddRange(["-sn", "-dn", "-progress", "pipe:1", "-nostats", output]);
        return args;
    }

    /// <summary>Position atteinte, d'après une ligne « out_time_us=… » de <c>-progress</c>.</summary>
    public static TimeSpan? ParseProgress(string line)
    {
        const string key = "out_time_us=";
        return line.StartsWith(key, StringComparison.Ordinal) &&
               long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0
            ? TimeSpan.FromTicks(us * 10)
            : null;
    }

    /// <summary>Lance un outil, transmet chaque ligne de sortie, renvoie la sortie ; tue le processus si annulé.</summary>
    private static async Task<string> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"impossible de lancer {Path.GetFileName(exe)}.");
        var output = new StringBuilder();
        var errors = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errors) errors.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        using var kill = ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        while (await p.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            onLine?.Invoke(line);
        }
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
        {
            string err;
            lock (errors) err = errors.ToString().Trim();
            throw new InvalidOperationException($"{Path.GetFileNameWithoutExtension(exe)} a échoué ({p.ExitCode}) : {err.Split('\n').FirstOrDefault()?.Trim()}");
        }
        return output.ToString();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
