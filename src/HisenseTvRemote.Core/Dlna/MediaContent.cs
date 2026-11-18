using Microsoft.Win32.SafeHandles;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>
/// Ce que le serveur envoie à la TV : un fichier tel quel, ou une vue réorganisée d'un fichier
/// (<see cref="InterleavedMp4"/>). Toujours lu dans le fichier d'origine <see cref="SourcePath"/>,
/// jamais recopié.
/// </summary>
public abstract class MediaContent(string sourcePath)
{
    public string SourcePath { get; } = sourcePath;

    /// <summary>Taille du contenu servi (peut différer de celle du fichier d'origine).</summary>
    public abstract long Length { get; }

    /// <summary>Lit à partir de <paramref name="position"/> du contenu servi ; 0 = fin.</summary>
    public abstract ValueTask<int> ReadAsync(SafeFileHandle source, long position, Memory<byte> buffer, CancellationToken ct);

    /// <summary>Octet du contenu servi correspondant à l'instant <paramref name="time"/> (estimation linéaire par défaut).</summary>
    public virtual long ByteAt(TimeSpan time, TimeSpan? duration) =>
        duration is { } d && d > TimeSpan.Zero ? (long)Math.Clamp(Length * (time / d), 0, Length) : 0;

    /// <summary>Instant correspondant à l'octet <paramref name="position"/> (estimation linéaire par défaut).</summary>
    public virtual TimeSpan? TimeAt(long position, TimeSpan? duration) =>
        duration is { } d && d > TimeSpan.Zero && Length > 0 ? d * Math.Clamp(position / (double)Length, 0, 1) : null;

    public SafeFileHandle OpenSource() =>
        File.OpenHandle(SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
}

/// <summary>
/// Plages d'octets déjà envoyées à la TV (fusionnées) : sert à afficher la partie « chargée » de la barre
/// de progression. Ce que la TV a reçu est en avance sur ce qu'elle a lu.
/// </summary>
public sealed class ByteRanges
{
    private readonly List<(long Start, long End)> _ranges = [];      // triées, disjointes, fin exclue

    public void Add(long start, long end)
    {
        if (end <= start) return;
        lock (_ranges)
        {
            var i = 0;
            while (i < _ranges.Count && _ranges[i].End < start) i++;
            while (i < _ranges.Count && _ranges[i].Start <= end)
            {
                start = Math.Min(start, _ranges[i].Start);
                end = Math.Max(end, _ranges[i].End);
                _ranges.RemoveAt(i);
            }
            _ranges.Insert(i, (start, end));
        }
    }

    /// <summary>Fin de la plage continue qui contient <paramref name="position"/> (ou la position elle-même).</summary>
    public long ContiguousEnd(long position)
    {
        lock (_ranges)
        {
            foreach (var (s, e) in _ranges)
                if (s <= position && position < e) return e;
            return position;
        }
    }
}

/// <summary>Le fichier tel quel.</summary>
public sealed class FileContent(string path) : MediaContent(path)
{
    public override long Length { get; } = new FileInfo(path).Length;

    public override ValueTask<int> ReadAsync(SafeFileHandle source, long position, Memory<byte> buffer, CancellationToken ct) =>
        position >= Length ? ValueTask.FromResult(0) : RandomAccess.ReadAsync(source, buffer, position, ct);
}
