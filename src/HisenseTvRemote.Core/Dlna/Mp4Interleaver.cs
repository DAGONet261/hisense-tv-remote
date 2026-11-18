using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>
/// Vue « réorganisée » d'un MP4/MOV, servie à la TV à la place du fichier d'origine, sans le recopier :
/// <c>ftyp</c> + <c>moov</c> réécrit + <c>mdat</c> dont les échantillons (images, morceaux de son) sont
/// rangés dans l'ordre chronologique, image et son alternés finement.
/// <para>
/// Pourquoi : le lecteur DLNA de la 55A53FXVT lit image et son séparément. Avec un MP4 rangé par gros
/// blocs (≈ 0,3 s d'image puis le son, typique des logiciels de montage), il revient sans cesse en
/// arrière, coupe et rouvre ses connexions, et n'atteint que ~1 Mo/s : la vidéo saccade. Rangé finement
/// (comme le fait FFmpeg), le même fichier est lu d'une traite, à 2-5 Mo/s (mesuré le 18/11).
/// </para>
/// Seules les tables d'emplacements changent (<c>stco</c>/<c>co64</c>, <c>stsc</c>) ; tailles,
/// horodatages, images clés et codecs sont repris tels quels. L'index est placé en tête (lecture immédiate).
/// </summary>
public sealed class InterleavedMp4 : MediaContent
{
    private readonly byte[] _header;
    private readonly long[] _virtualStart;
    private readonly long[] _sourceOffset;
    private readonly int[] _size;
    private readonly double[] _time;

    internal InterleavedMp4(string path, byte[] header, long[] virtualStart, long[] sourceOffset, int[] size, double[] time, long length) : base(path)
    {
        _time = time;
        _header = header;
        _virtualStart = virtualStart;
        _sourceOffset = sourceOffset;
        _size = size;
        Length = length;
    }

    public override long Length { get; }

    /// <summary>Taille de l'en-tête réécrit (ftyp + moov + en-tête mdat).</summary>
    public int HeaderLength => _header.Length;

    /// <summary>Nombre de morceaux (échantillons ou blocs) rangés dans le mdat.</summary>
    public int UnitCount => _size.Length;

    /// <summary>Exact : les morceaux sont rangés par instant de début.</summary>
    public override long ByteAt(TimeSpan time, TimeSpan? duration)
    {
        if (_virtualStart.Length == 0) return _header.Length;
        var i = Array.BinarySearch(_time, time.TotalSeconds);
        if (i < 0) i = Math.Max(0, ~i - 1);
        while (i > 0 && _time[i - 1] == _time[i]) i--;                  // premier morceau de cet instant
        return _virtualStart[i];
    }

    public override TimeSpan? TimeAt(long position, TimeSpan? duration)
    {
        if (_virtualStart.Length == 0) return null;
        if (position <= _header.Length) return TimeSpan.Zero;
        if (position >= Length) return duration ?? TimeSpan.FromSeconds(_time[^1]);
        var i = Array.BinarySearch(_virtualStart, position);
        if (i < 0) i = ~i - 1;
        return TimeSpan.FromSeconds(_time[Math.Clamp(i, 0, _time.Length - 1)]);
    }

    public override async ValueTask<int> ReadAsync(SafeFileHandle source, long position, Memory<byte> buffer, CancellationToken ct)
    {
        var written = 0;
        while (written < buffer.Length && position < Length)
        {
            int n;
            if (position < _header.Length)
            {
                n = (int)Math.Min(buffer.Length - written, _header.Length - position);
                _header.AsSpan((int)position, n).CopyTo(buffer.Span[written..]);
            }
            else
            {
                var i = Array.BinarySearch(_virtualStart, position);
                if (i < 0) i = ~i - 1;
                var inUnit = position - _virtualStart[i];
                var want = (int)Math.Min(buffer.Length - written, _size[i] - inUnit);
                n = await RandomAccess.ReadAsync(source, buffer.Slice(written, want), _sourceOffset[i] + inUnit, ct).ConfigureAwait(false);
                if (n <= 0) break;                                      // fichier tronqué : on s'arrête
            }
            written += n;
            position += n;
        }
        return written;
    }
}

/// <summary>Construit un <see cref="InterleavedMp4"/> ; null si le fichier ne s'y prête pas (il part alors tel quel).</summary>
public static class Mp4Interleaver
{
    private const int MaxMoovBytes = 256 * 1024 * 1024;
    private static readonly HashSet<string> Containers = ["moov", "trak", "mdia", "minf", "stbl", "edts", "dinf"];

    /// <summary>
    /// Null si : pas un MP4/MOV lisible, MP4 fragmenté (<c>moof</c>), données externes (<c>dref</c>),
    /// table compacte <c>stz2</c>, ou incohérence. Ne lève pas pour un fichier simplement inadapté.
    /// </summary>
    public static InterleavedMp4? TryCreate(string path)
    {
        try { return Create(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or OverflowException
                                       or IndexOutOfRangeException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static InterleavedMp4 Create(string path)
    {
        byte[]? ftyp = null, moov = null;
        long fileLength;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            fileLength = fs.Length;
            long pos = 0;
            var head = new byte[16];
            while (pos + 8 <= fileLength)
            {
                fs.Position = pos;
                fs.ReadExactly(head, 0, 8);
                long size = BinaryPrimitives.ReadUInt32BigEndian(head);
                var type = Encoding.ASCII.GetString(head, 4, 4);
                var headerSize = 8;
                if (size == 1) { fs.ReadExactly(head, 8, 8); size = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(8)); headerSize = 16; }
                else if (size == 0) size = fileLength - pos;
                if (size < headerSize || pos + size > fileLength) throw new InvalidDataException($"boîte {type} invalide");
                if (type == "moof") throw new InvalidDataException("MP4 fragmenté");
                if (type is "ftyp" or "moov")
                {
                    if (size > MaxMoovBytes) throw new InvalidDataException("index trop grand");
                    var box = new byte[size];
                    fs.Position = pos;
                    fs.ReadExactly(box);
                    if (type == "ftyp") ftyp = box; else moov = box;
                }
                pos += size;
            }
        }
        if (moov is null) throw new InvalidDataException("pas d'index moov");
        ftyp ??= [];

        // ---- Pistes et échantillons ----------------------------------------------------------
        var tracks = new List<Track>();
        foreach (var trak in Children(moov, Box.Of(moov, 0)).Where(b => b.Type == "trak"))
            tracks.Add(Track.Read(moov, trak, fileLength, tracks.Count));
        if (tracks.Count == 0 || tracks.All(t => t.Units.Count == 0)) throw new InvalidDataException("aucun échantillon");

        // Ordre chronologique (début de chaque morceau), à égalité : ordre des pistes puis d'origine.
        var all = tracks.SelectMany(t => t.Units).ToArray();
        Array.Sort(all, (a, b) =>
        {
            var c = a.Time.CompareTo(b.Time);
            if (c != 0) return c;
            c = a.Track.CompareTo(b.Track);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });
        long payload = 0;
        foreach (var u in all) { u.Relative = payload; payload += u.Size; }

        var mdatHeader = payload + 8 > uint.MaxValue ? 16 : 8;
        var useCo64 = ftyp.Length + (long)moov.Length * 2 + 64L * all.Length + mdatHeader + payload > uint.MaxValue;

        // Deux passes : la taille du moov ne dépend que du nombre d'entrées, pas des valeurs.
        var newMoov = BuildMoov(moov, tracks, 0, useCo64);
        var dataStart = ftyp.Length + newMoov.Length + mdatHeader;
        newMoov = BuildMoov(moov, tracks, dataStart, useCo64);

        var header = new byte[ftyp.Length + newMoov.Length + mdatHeader];
        ftyp.CopyTo(header, 0);
        newMoov.CopyTo(header, ftyp.Length);
        var m = header.AsSpan(ftyp.Length + newMoov.Length);
        if (mdatHeader == 8) { BinaryPrimitives.WriteUInt32BigEndian(m, (uint)(payload + 8)); Encoding.ASCII.GetBytes("mdat", m[4..]); }
        else { BinaryPrimitives.WriteUInt32BigEndian(m, 1); Encoding.ASCII.GetBytes("mdat", m[4..]); BinaryPrimitives.WriteUInt64BigEndian(m[8..], (ulong)(payload + 16)); }

        var vStart = new long[all.Length];
        var src = new long[all.Length];
        var sizes = new int[all.Length];
        var times = new double[all.Length];
        for (var i = 0; i < all.Length; i++) { vStart[i] = dataStart + all[i].Relative; src[i] = all[i].Source; sizes[i] = all[i].Size; times[i] = all[i].Time; }
        return new InterleavedMp4(path, header, vStart, src, sizes, times, dataStart + payload);
    }

    // ---------- Réécriture du moov -------------------------------------------------------------

    private static byte[] BuildMoov(byte[] moov, List<Track> tracks, long dataStart, bool useCo64)
    {
        using var ms = new MemoryStream(moov.Length + 64 * tracks.Sum(t => t.Units.Count));
        var trackIndex = 0;
        WriteBox(ms, moov, Box.Of(moov, 0), box =>
        {
            if (box.Type != "trak") return null;
            return tracks[trackIndex++];
        }, null, dataStart, useCo64);
        return ms.ToArray();
    }

    /// <summary>Recopie une boîte ; dans le stbl de chaque piste, remplace stsc et stco/co64.</summary>
    private static void WriteBox(MemoryStream ms, byte[] data, Box box, Func<Box, Track?> onTrak, Track? track, long dataStart, bool useCo64)
    {
        if (!Containers.Contains(box.Type))
        {
            ms.Write(data, (int)box.Start, (int)box.Size);
            return;
        }
        var start = ms.Position;
        ms.Write(new byte[8]);                                               // taille et type, écrits à la fin
        if (box.Type == "trak") track = onTrak(box);
        foreach (var child in Children(data, box))
        {
            if (box.Type == "stbl" && track is not null)
            {
                if (child.Type == "stsc") { ms.Write(track.BuildStsc()); continue; }
                if (child.Type is "stco" or "co64") { ms.Write(track.BuildOffsets(dataStart, useCo64)); continue; }
            }
            WriteBox(ms, data, child, onTrak, track, dataStart, useCo64);
        }
        var end = ms.Position;
        var size = end - start;
        ms.Position = start;
        Span<byte> h = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h, (uint)size);
        Encoding.ASCII.GetBytes(box.Type, h[4..]);
        ms.Write(h);
        ms.Position = end;
    }

    // ---------- Lecture des boîtes --------------------------------------------------------------

    internal readonly record struct Box(string Type, long Start, int HeaderSize, long Size)
    {
        public long PayloadStart => Start + HeaderSize;
        public long End => Start + Size;

        public static Box Of(byte[] data, long start)
        {
            if (start + 8 > data.Length) throw new InvalidDataException("boîte tronquée");
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)start));
            var type = Encoding.ASCII.GetString(data, (int)start + 4, 4);
            var hs = 8;
            if (size == 1) { size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan((int)start + 8)); hs = 16; }
            else if (size == 0) size = data.Length - start;
            if (size < hs || start + size > data.Length) throw new InvalidDataException($"boîte {type} tronquée");
            return new Box(type, start, hs, size);
        }
    }

    internal static IEnumerable<Box> Children(byte[] data, Box parent)
    {
        var pos = parent.PayloadStart;
        while (pos + 8 <= parent.End)
        {
            var b = Box.Of(data, pos);
            yield return b;
            pos = b.End;
        }
    }

    private static Box? Find(byte[] data, Box parent, params string[] path)
    {
        Box? cur = parent;
        foreach (var name in path)
        {
            cur = Children(data, cur.Value).Cast<Box?>().FirstOrDefault(b => b!.Value.Type == name);
            if (cur is null) return null;
        }
        return cur;
    }

    private static uint U32(byte[] d, long at) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan((int)at));
    private static ulong U64(byte[] d, long at) => BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan((int)at));

    // ---------- Une piste --------------------------------------------------------------------------

    /// <summary>Un morceau du mdat : un échantillon (taille variable) ou un bloc d'échantillons de taille fixe (PCM).</summary>
    private sealed class Unit(int track, int index, double time, long source, int size)
    {
        public int Track { get; } = track;
        public int Index { get; } = index;
        public double Time { get; } = time;
        public long Source { get; } = source;
        public int Size { get; } = size;
        public long Relative { get; set; }
    }

    private sealed class Track
    {
        public List<Unit> Units { get; } = [];
        private uint[] _unitDescription = [];
        private byte[]? _originalStsc;
        private bool _perSample;

        public static Track Read(byte[] moov, Box trak, long fileLength, int number)
        {
            var mdhd = Find(moov, trak, "mdia", "mdhd") ?? throw new InvalidDataException("mdhd absent");
            var stbl = Find(moov, trak, "mdia", "minf", "stbl") ?? throw new InvalidDataException("stbl absent");
            var dref = Find(moov, trak, "mdia", "minf", "dinf", "dref");
            if (dref is { } dr)
            {
                // Chaque entrée (url/alis) doit porter le drapeau « données dans ce fichier » (1).
                var entryPos = dr.PayloadStart + 8;
                var count = U32(moov, dr.PayloadStart + 4);
                for (var i = 0; i < count; i++)
                {
                    var e = Box.Of(moov, entryPos);
                    if ((U32(moov, e.PayloadStart) & 1) == 0) throw new InvalidDataException("données externes");
                    entryPos = e.End;
                }
            }

            var version = moov[mdhd.PayloadStart];
            var timescale = U32(moov, mdhd.PayloadStart + (version == 1 ? 20 : 12));
            if (timescale == 0) throw new InvalidDataException("échelle de temps nulle");

            var boxes = Children(moov, stbl).ToDictionary(b => b.Type, b => b, StringComparer.Ordinal);
            if (boxes.ContainsKey("stz2")) throw new InvalidDataException("stz2 non géré");
            if (!boxes.TryGetValue("stsz", out var stsz) || !boxes.TryGetValue("stsc", out var stsc) || !boxes.TryGetValue("stts", out var stts))
                throw new InvalidDataException("tables d'échantillons absentes");

            // Emplacements des blocs d'origine.
            long[] chunkOffsets;
            if (boxes.TryGetValue("co64", out var co64))
            {
                chunkOffsets = new long[U32(moov, co64.PayloadStart + 4)];
                for (var i = 0; i < chunkOffsets.Length; i++) chunkOffsets[i] = (long)U64(moov, co64.PayloadStart + 8 + 8L * i);
            }
            else if (boxes.TryGetValue("stco", out var stco))
            {
                chunkOffsets = new long[U32(moov, stco.PayloadStart + 4)];
                for (var i = 0; i < chunkOffsets.Length; i++) chunkOffsets[i] = U32(moov, stco.PayloadStart + 8 + 4L * i);
            }
            else throw new InvalidDataException("stco absent");

            // Échantillons par bloc et description, bloc par bloc (stsc).
            var stscCount = (int)U32(moov, stsc.PayloadStart + 4);
            var perChunk = new uint[chunkOffsets.Length];
            var description = new uint[chunkOffsets.Length];
            for (var e = 0; e < stscCount; e++)
            {
                var at = stsc.PayloadStart + 8 + 12L * e;
                var first = (int)U32(moov, at) - 1;
                var spc = U32(moov, at + 4);
                var sdi = U32(moov, at + 8);
                var next = e + 1 < stscCount ? (int)U32(moov, at + 12) - 1 : chunkOffsets.Length;
                for (var c = Math.Max(0, first); c < Math.Min(next, chunkOffsets.Length); c++) { perChunk[c] = spc; description[c] = sdi; }
            }

            var constantSize = U32(moov, stsz.PayloadStart + 4);
            var sampleCount = U32(moov, stsz.PayloadStart + 8);
            if (perChunk.Sum(x => (long)x) != sampleCount) throw new InvalidDataException("tables incohérentes");

            // Horodatages (stts) : on avance échantillon par échantillon, ou par blocs entiers pour le PCM.
            var sttsCount = (int)U32(moov, stts.PayloadStart + 4);
            var sttsEntry = 0;
            long sttsLeft = sttsCount > 0 ? U32(moov, stts.PayloadStart + 8) : 0;
            long sttsDelta = sttsCount > 0 ? U32(moov, stts.PayloadStart + 12) : 0;
            long dts = 0;
            void Advance(long samples)
            {
                while (samples > 0 && sttsEntry < sttsCount)
                {
                    var step = Math.Min(samples, sttsLeft);
                    dts += step * sttsDelta;
                    samples -= step;
                    sttsLeft -= step;
                    if (sttsLeft == 0 && ++sttsEntry < sttsCount)
                    {
                        sttsLeft = U32(moov, stts.PayloadStart + 8 + 8L * sttsEntry);
                        sttsDelta = U32(moov, stts.PayloadStart + 12 + 8L * sttsEntry);
                    }
                }
            }

            var track = new Track { _perSample = constantSize == 0 };
            var descriptions = new List<uint>();
            long sample = 0;
            for (var c = 0; c < chunkOffsets.Length; c++)
            {
                var offset = chunkOffsets[c];
                if (constantSize != 0)
                {
                    // Taille fixe (PCM…) : on garde les blocs d'origine (des millions d'échantillons sinon).
                    var size = checked((int)(perChunk[c] * (long)constantSize));
                    if (offset + size > fileLength) throw new InvalidDataException("bloc hors du fichier");
                    track.Units.Add(new Unit(number, track.Units.Count, dts / (double)timescale, offset, size));   // même vide : 1 bloc = 1 entrée stco
                    Advance(perChunk[c]);
                }
                else
                {
                    for (var s = 0; s < perChunk[c]; s++, sample++)
                    {
                        var size = (int)U32(moov, stsz.PayloadStart + 12 + 4 * sample);
                        if (offset + size > fileLength) throw new InvalidDataException("échantillon hors du fichier");
                        track.Units.Add(new Unit(number, track.Units.Count, dts / (double)timescale, offset, size));
                        descriptions.Add(description[c]);
                        offset += size;
                        Advance(1);
                    }
                }
            }
            if (constantSize == 0 && sample != sampleCount) throw new InvalidDataException("nombre d'échantillons incohérent");
            track._unitDescription = [.. descriptions];
            track._originalStsc = moov.AsSpan((int)stsc.Start, (int)stsc.Size).ToArray();
            return track;
        }

        /// <summary>
        /// Taille variable : un échantillon par bloc (description reprise par séries).
        /// Taille fixe : blocs d'origine, donc stsc d'origine.
        /// </summary>
        public byte[] BuildStsc()
        {
            if (!_perSample) return _originalStsc!;
            var entries = new List<(uint First, uint Sdi)>();
            for (var i = 0; i < _unitDescription.Length; i++)
                if (i == 0 || _unitDescription[i] != _unitDescription[i - 1]) entries.Add(((uint)i + 1, _unitDescription[i]));
            var box = new byte[16 + 12 * entries.Count];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            Encoding.ASCII.GetBytes("stsc", box.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12), (uint)entries.Count);
            for (var e = 0; e < entries.Count; e++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16 + 12 * e), entries[e].First);
                BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20 + 12 * e), 1);
                BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(24 + 12 * e), entries[e].Sdi);
            }
            return box;
        }

        /// <summary>Nouvel emplacement de chaque bloc : stco (32 bits) ou co64.</summary>
        public byte[] BuildOffsets(long dataStart, bool useCo64)
        {
            var width = useCo64 ? 8 : 4;
            var box = new byte[16 + width * Units.Count];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            Encoding.ASCII.GetBytes(useCo64 ? "co64" : "stco", box.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12), (uint)Units.Count);
            for (var i = 0; i < Units.Count; i++)
            {
                var at = dataStart + Units[i].Relative;
                if (useCo64) BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(16 + 8 * i), (ulong)at);
                else BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16 + 4 * i), checked((uint)at));
            }
            return box;
        }
    }
}
