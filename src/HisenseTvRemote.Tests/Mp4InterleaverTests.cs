using System.Buffers.Binary;
using System.Text;
using HisenseTvRemote.Core.Dlna;

namespace HisenseTvRemote.Tests;

/// <summary>
/// Réorganisation des MP4 (partage DLNA) sur de petits MP4 construits ici : index en fin de fichier,
/// vidéo et son rangés par gros blocs, piste PCM à taille fixe. La vérification sur un vrai fichier
/// (4,6 Go, empreintes FFmpeg identiques paquet par paquet) est décrite dans contexte/partage-dlna.md.
/// </summary>
public class Mp4InterleaverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mp4-" + Guid.NewGuid().ToString("N"));

    public Mp4InterleaverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ---------- Construction d'un MP4 minimal ----------------------------------------------------

    private static byte[] Box(string type, params byte[][] parts)
    {
        var payload = parts.SelectMany(p => p).ToArray();
        var b = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)b.Length);
        Encoding.ASCII.GetBytes(type, b.AsSpan(4));
        payload.CopyTo(b, 8);
        return b;
    }

    private static byte[] U32(params uint[] values)
    {
        var b = new byte[4 * values.Length];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4 * i), values[i]);
        return b;
    }

    /// <summary>Piste : échantillons (contenu), blocs (nombre d'échantillons par bloc), échelle et durée d'un échantillon.</summary>
    private sealed record TrackSpec(byte[][] Samples, int[] Chunks, uint Timescale, uint Delta, uint ConstantSize = 0);

    private static byte[] Sample(int track, int index, int size) =>
        [.. Enumerable.Range(0, size).Select(i => (byte)(track * 64 + index * 7 + i))];

    /// <summary>MP4 : ftyp, mdat (pistes rangées l'une après l'autre, par blocs), puis moov à la fin.</summary>
    private static byte[] BuildMp4(IReadOnlyList<TrackSpec> tracks, bool fragmented = false)
    {
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), U32(512), Encoding.ASCII.GetBytes("isomiso2"));
        var data = new List<byte>();
        var chunkOffsets = new List<List<uint>>();
        var dataStart = (uint)(ftyp.Length + 8);
        foreach (var t in tracks)
        {
            var offsets = new List<uint>();
            var s = 0;
            foreach (var count in t.Chunks)
            {
                offsets.Add(dataStart + (uint)data.Count);
                for (var k = 0; k < count; k++) data.AddRange(t.Samples[s++]);
            }
            chunkOffsets.Add(offsets);
        }
        var mdat = Box("mdat", [.. data]);

        var traks = tracks.Select((t, i) =>
        {
            var stsc = U32([(uint)t.Chunks.Length, .. t.Chunks.SelectMany((c, k) => new uint[] { (uint)k + 1, (uint)c, 1 })]);
            var stsz = t.ConstantSize != 0
                ? U32(0, t.ConstantSize, (uint)t.Samples.Length)
                : U32([0, 0, (uint)t.Samples.Length, .. t.Samples.Select(x => (uint)x.Length)]);
            var stbl = Box("stbl",
                Box("stsd", U32(0, 0)),
                Box("stts", U32(0, 1, (uint)t.Samples.Length, t.Delta)),
                Box("stsc", U32(0), stsc),
                Box("stsz", stsz),
                Box("stco", U32([0, (uint)chunkOffsets[i].Count, .. chunkOffsets[i]])));
            var dinf = Box("dinf", Box("dref", U32(0, 1), Box("url ", U32(1))));
            return Box("trak",
                Box("mdia",
                    Box("mdhd", U32(0, 0, 0, t.Timescale, 0, 0)),
                    Box("hdlr", U32(0, 0), Encoding.ASCII.GetBytes("vide"), U32(0, 0, 0), [0]),
                    Box("minf", dinf, stbl)));
        }).ToArray();
        var moov = Box("moov", [Box("mvhd", U32(0, 0, 0, 1000, 0)), .. traks]);
        return [.. ftyp, .. mdat, .. moov, .. (fragmented ? Box("moof", U32(0)) : [])];
    }

    private string Write(byte[] bytes)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static async Task<byte[]> ReadAll(MediaContent content, int piece)
    {
        using var h = content.OpenSource();
        var result = new byte[content.Length];
        long pos = 0;
        while (pos < content.Length)
        {
            var n = await content.ReadAsync(h, pos, result.AsMemory((int)pos, (int)Math.Min(piece, content.Length - pos)), CancellationToken.None);
            Assert.True(n > 0);
            pos += n;
        }
        Assert.Equal(0, await content.ReadAsync(h, pos, new byte[10], CancellationToken.None));
        return result;
    }

    // ---------- Lecture d'un MP4 (côté test, indépendante du code testé) --------------------------

    private static (string Type, int Start, int Size)[] TopLevel(byte[] f)
    {
        var list = new List<(string, int, int)>();
        for (var p = 0; p + 8 <= f.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(p));
            list.Add((Encoding.ASCII.GetString(f, p + 4, 4), p, size));
            p += size;
        }
        return [.. list];
    }

    private static IEnumerable<(string Type, int Start, int Size)> Kids(byte[] f, int start, int size)
    {
        for (var p = start + 8; p + 8 <= start + size;)
        {
            var s = (int)BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(p));
            yield return (Encoding.ASCII.GetString(f, p + 4, 4), p, s);
            p += s;
        }
    }

    /// <summary>Contenu de chaque échantillon, piste par piste, d'après stsz/stsc/stco du fichier.</summary>
    private static List<List<byte[]>> SamplesOf(byte[] f)
    {
        uint R(int at) => BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at));
        var moov = TopLevel(f).Single(b => b.Type == "moov");
        var result = new List<List<byte[]>>();
        foreach (var trak in Kids(f, moov.Start, moov.Size).Where(b => b.Type == "trak"))
        {
            var mdia = Kids(f, trak.Start, trak.Size).Single(b => b.Type == "mdia");
            var minf = Kids(f, mdia.Start, mdia.Size).Single(b => b.Type == "minf");
            var stbl = Kids(f, minf.Start, minf.Size).Single(b => b.Type == "stbl");
            var k = Kids(f, stbl.Start, stbl.Size).ToDictionary(b => b.Type);
            var stsz = k["stsz"].Start + 8;
            var constant = R(stsz + 4);
            var count = (int)R(stsz + 8);
            var co = k.TryGetValue("co64", out var c64) ? c64 : k["stco"];
            var chunks = (int)R(co.Start + 12);
            long Offset(int i) => co.Type == "co64" ? (long)BinaryPrimitives.ReadUInt64BigEndian(f.AsSpan(co.Start + 16 + 8 * i)) : R(co.Start + 16 + 4 * i);
            var stsc = k["stsc"].Start + 8;
            var entries = (int)R(stsc + 4);
            var samples = new List<byte[]>();
            var s = 0;
            for (var c = 0; c < chunks; c++)
            {
                var e = Enumerable.Range(0, entries).Last(x => R(stsc + 8 + 12 * x) <= c + 1);
                var perChunk = (int)R(stsc + 12 + 12 * e);
                var off = Offset(c);
                for (var j = 0; j < perChunk; j++, s++)
                {
                    var size = constant != 0 ? (int)constant : (int)R(stsz + 12 + 4 * s);
                    samples.Add(f.AsSpan((int)off, size).ToArray());
                    off += size;
                }
            }
            Assert.Equal(count, s);
            result.Add(samples);
        }
        return result;
    }

    // ---------- Tests -------------------------------------------------------------------------

    private static TrackSpec[] ThreeTracks() =>
    [
        // Vidéo : 5 images en un seul bloc (30 i/s).
        new([.. new[] { 100, 50, 60, 70, 80 }.Select((n, i) => Sample(0, i, n))], [5], 30, 1),
        // Son : 8 morceaux en 2 blocs (20 ms chacun).
        new([.. Enumerable.Range(0, 8).Select(i => Sample(1, i, 10 + i))], [4, 4], 1000, 20),
        // PCM : 10 échantillons de 2 octets, 2 blocs (taille fixe : blocs conservés).
        new([.. Enumerable.Range(0, 10).Select(i => Sample(2, i, 2))], [5, 5], 100, 1, ConstantSize: 2),
    ];

    [Fact]
    public async Task Echantillons_identiques_index_en_tete_et_ordre_chronologique()
    {
        var specs = ThreeTracks();
        var original = BuildMp4(specs);
        var path = Write(original);
        var view = Mp4Interleaver.TryCreate(path)!;
        Assert.NotNull(view);

        var served = await ReadAll(view, 1 << 20);
        Assert.Equal(served, await ReadAll(view, 7));                          // lecture par petits morceaux : idem
        Assert.Equal(["ftyp", "moov", "mdat"], TopLevel(served).Select(b => b.Type));   // index en tête

        var before = SamplesOf(original);
        var after = SamplesOf(served);
        Assert.Equal(before.Count, after.Count);
        for (var t = 0; t < before.Count; t++) Assert.Equal(before[t], after[t]);        // octet pour octet

        // Ordre dans le mdat : par instant de début (vidéo 0 s, son 0 s, PCM 0 s, son 20 ms…).
        var mdat = TopLevel(served).Single(b => b.Type == "mdat");
        var expected = new byte[][] { specs[0].Samples[0], specs[1].Samples[0], specs[2].Samples[0].Concat(specs[2].Samples[1])
            .Concat(specs[2].Samples[2]).Concat(specs[2].Samples[3]).Concat(specs[2].Samples[4]).ToArray(), specs[1].Samples[1] };
        var at = mdat.Start + 8;
        foreach (var e in expected)
        {
            Assert.Equal(e, served.AsSpan(at, e.Length).ToArray());
            at += e.Length;
        }
        Assert.Equal(view.Length, served.Length);

        // Octets ↔ temps exacts : le PCM de 0,03 s (3e bloc d'échantillons : 0,05 s) est au-delà de l'en-tête.
        Assert.Equal(TimeSpan.Zero, view.TimeAt(0, null));
        Assert.Equal(view.HeaderLength, view.ByteAt(TimeSpan.Zero, null));
        var at40ms = view.ByteAt(TimeSpan.FromMilliseconds(40), null);                             // son n° 2 (40 ms)
        Assert.Equal(TimeSpan.FromMilliseconds(40), view.TimeAt(at40ms, null));
        Assert.True(at40ms > view.HeaderLength);
    }

    [Fact]
    public void Fichiers_non_reorganisables()
    {
        Assert.Null(Mp4Interleaver.TryCreate(Write(Encoding.ASCII.GetBytes("pas une vidéo du tout"))));
        Assert.Null(Mp4Interleaver.TryCreate(Write(BuildMp4(ThreeTracks(), fragmented: true))));   // MP4 fragmenté
        var truncated = BuildMp4(ThreeTracks());
        Assert.Null(Mp4Interleaver.TryCreate(Write(truncated[..^20])));                         // index tronqué
    }

    [Fact]
    public async Task Vue_servie_par_le_serveur_avec_plages()
    {
        var path = Write(BuildMp4(ThreeTracks()));
        var view = Mp4Interleaver.TryCreate(path)!;
        var whole = await ReadAll(view, 4096);
        await using var server = new MediaHttpServer(System.Net.IPAddress.Loopback, [System.Net.IPAddress.Loopback], 0, System.Net.IPAddress.Loopback);
        var url = server.Publish(view, MediaTypes.Of(path)!);
        using var http = new HttpClient();
        Assert.Equal(whole, await http.GetByteArrayAsync(url));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(whole.Length - 100, null);       // fin du fichier
        using var res = await http.SendAsync(req);
        Assert.Equal(whole[^100..], await res.Content.ReadAsByteArrayAsync());
    }
}
