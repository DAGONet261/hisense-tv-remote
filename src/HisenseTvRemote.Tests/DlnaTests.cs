using System.Net;
using System.Net.Http.Headers;
using HisenseTvRemote.Core.Dlna;
using HisenseTvRemote.Core.ViewModels;

namespace HisenseTvRemote.Tests;

/// <summary>Partage vers la TV (DLNA) : formats, plages d'octets, SOAP, serveur HTTP local, écran de partage.</summary>
public class DlnaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dlna-" + Guid.NewGuid().ToString("N"));

    public DlnaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string File(string name, int bytes = 100)
    {
        var p = Path.Combine(_dir, name);
        System.IO.File.WriteAllBytes(p, [.. Enumerable.Range(0, bytes).Select(i => (byte)i)]);
        return p;
    }

    [Fact]
    public void Formats_reconnus_et_protocolInfo()
    {
        Assert.Equal(new MediaType("image/jpeg", MediaKind.Photo), MediaTypes.Of(@"C:\x\Photo.JPG"));
        Assert.Equal(MediaKind.Photo, MediaTypes.Of("iphone.heic")!.Kind);                    // converti en JPEG
        Assert.Equal("image/jpeg", MediaTypes.Of("iphone.heic")!.Mime);
        Assert.Equal(new MediaType("video/x-matroska", MediaKind.Video), MediaTypes.Of("film.mkv"));
        Assert.Equal(MediaKind.Audio, MediaTypes.Of("chanson.flac")!.Kind);
        Assert.Null(MediaTypes.Of("notes.txt"));

        var jpeg = MediaTypes.Of("a.jpg")!;
        Assert.StartsWith("http-get:*:image/jpeg:DLNA.ORG_PN=JPEG_LRG;DLNA.ORG_OP=00", jpeg.ProtocolInfo);
        Assert.Equal("object.item.imageItem.photo", jpeg.UpnpClass);
        var mp4 = MediaTypes.Of("a.mp4")!;
        Assert.Contains("DLNA.ORG_OP=01", mp4.ProtocolInfo);                                      // avance/recul par octets
        Assert.Equal("DLNA.ORG_OP=01;DLNA.ORG_FLAGS=01700000000000000000000000000000", mp4.ContentFeatures);
        Assert.Contains("*.heic", MediaTypes.OpenFileFilter);
    }

    [Theory]
    [InlineData(null, 1000, null, null)]
    [InlineData("bytes=0-99", 1000, 0L, 99L)]
    [InlineData("bytes=500-", 1000, 500L, 999L)]
    [InlineData("bytes=-100", 1000, 900L, 999L)]
    [InlineData("bytes=0-5000", 1000, 0L, 999L)]                 // fin bornée à la taille
    [InlineData("bytes=0-1,5-9", 1000, null, null)]              // plages multiples : fichier entier
    [InlineData("items=0-1", 1000, null, null)]
    public void Plages_d_octets(string? header, long size, long? start, long? end)
    {
        var r = ByteRange.Parse(header, size);
        if (start is null) Assert.Null(r);
        else Assert.Equal(new ByteRange(start.Value, end!.Value), r);
    }

    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=50-10")]
    [InlineData("bytes=-0")]
    public void Plages_hors_fichier(string header) => Assert.True(ByteRange.Parse(header, 1000)!.Value.IsUnsatisfiable);

    [Fact]
    public void Metadonnees_DIDL_echappees()
    {
        var didl = DlnaRenderer.Didl("http://pc:1/media/x.jpg?a=1&b=2", "Tom & Jerry <1>", MediaTypes.Of("a.jpg")!);
        Assert.Contains("<dc:title>Tom &amp; Jerry &lt;1&gt;</dc:title>", didl);
        Assert.Contains("http://pc:1/media/x.jpg?a=1&amp;b=2</res>", didl);
        Assert.Contains("<upnp:class>object.item.imageItem.photo</upnp:class>", didl);
        Assert.Contains("duration=\"0:56:20.608\"", DlnaRenderer.Didl("u", "t", MediaTypes.Of("a.mp4")!, new TimeSpan(0, 0, 56, 20, 608)));
        _ = System.Xml.Linq.XDocument.Parse(didl);                                                 // XML valide
    }

    [Fact]
    public void Temps_UPnP()
    {
        Assert.Equal(TimeSpan.FromSeconds(83), DlnaRenderer.ParseTime("0:01:23"));
        Assert.Equal(new TimeSpan(0, 1, 2, 3, 500), DlnaRenderer.ParseTime("01:02:03.500"));
        Assert.Null(DlnaRenderer.ParseTime("NOT_IMPLEMENTED"));
        Assert.Null(DlnaRenderer.ParseTime(""));
        Assert.Equal("0:01:23", DlnaRenderer.FormatTime(TimeSpan.FromSeconds(83)));
        Assert.Equal("1:00:05", DlnaRenderer.FormatTime(TimeSpan.FromSeconds(3605)));
        Assert.Equal("0:00:00", DlnaRenderer.FormatTime(TimeSpan.FromSeconds(-5)));
        Assert.Equal("1:23 / 45:00", ShareViewModel.FormatPosition(TimeSpan.FromSeconds(83), TimeSpan.FromMinutes(45)));
    }

    [Fact]
    public void Description_du_lecteur_de_la_TV()
    {
        // Extrait de la description réelle de la 55A53FXVT (17/11).
        const string xml = """
            <root xmlns="urn:schemas-upnp-org:device-1-0"><device>
              <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType><friendlyName>TV Salon</friendlyName>
              <serviceList>
                <service><serviceType>urn:schemas-upnp-org:service:ConnectionManager:1</serviceType><controlURL>/upnp/control/minguscmr</controlURL></service>
                <service><serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType><controlURL>/upnp/control/mingusavtr</controlURL></service>
                <service><serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType><controlURL>/upnp/control/mingusrcr</controlURL></service>
              </serviceList></device></root>
            """;
        using var http = new HttpClient();
        var r = DlnaRenderer.Parse(xml, new Uri("http://192.168.88.19:38400/MediaServer/rendererdevicedesc.xml"), http)!;
        Assert.Equal("TV Salon", r.Name);
        Assert.Equal(new Uri("http://192.168.88.19:38400/upnp/control/mingusavtr"), r.AvTransportUrl);
        Assert.Equal(new Uri("http://192.168.88.19:38400/upnp/control/mingusrcr"), r.RenderingControlUrl);

        var withBase = xml.Replace("<device>", "<URLBase>http://10.0.0.5:5000/</URLBase><device>");
        Assert.Equal(new Uri("http://10.0.0.5:5000/upnp/control/mingusavtr"),
            DlnaRenderer.Parse(withBase, new Uri("http://192.168.88.19:38400/d.xml"), http)!.AvTransportUrl);
        Assert.Null(DlnaRenderer.Parse("<root><device/></root>", new Uri("http://x/"), http));          // pas un lecteur
    }

    [Fact]
    public async Task Serveur_HTTP_fichier_entier_plage_HEAD_404()
    {
        var path = File("video.mp4", 1000);
        await using var server = new MediaHttpServer(IPAddress.Loopback, [IPAddress.Loopback], 0, IPAddress.Loopback);
        var url = server.Publish(path, MediaTypes.Of(path)!);
        Assert.DoesNotContain("video", url);                                                       // nom du fichier caché
        using var http = new HttpClient();

        using (var full = await http.GetAsync(url))
        {
            Assert.Equal(HttpStatusCode.OK, full.StatusCode);
            Assert.Equal(1000, (await full.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal("video/mp4", full.Content.Headers.ContentType!.MediaType);
            Assert.Equal("Streaming", full.Headers.GetValues("transferMode.dlna.org").Single());
            Assert.Contains("DLNA.ORG_OP=01", full.Headers.GetValues("contentFeatures.dlna.org").Single());
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, url))
        {
            req.Headers.Range = new RangeHeaderValue(10, 19);
            using var part = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
            Assert.Equal("bytes 10-19/1000", part.Content.Headers.ContentRange!.ToString());
            Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), await part.Content.ReadAsByteArrayAsync());
        }

        using (var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url)))
        {
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Equal(1000, head.Content.Headers.ContentLength);
        }

        using (var missing = await http.GetAsync(server.BaseUrl + "/media/inconnu.mp4"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        server.Unpublish(url);
        using (var gone = await http.GetAsync(url))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Serveur_HTTP_refuse_les_autres_appareils()
    {
        var path = File("photo.jpg");
        await using var server = new MediaHttpServer(IPAddress.Loopback, [IPAddress.Parse("192.168.88.19")], 0, IPAddress.Loopback);
        var url = server.Publish(path, MediaTypes.Of(path)!);
        using var http = new HttpClient();
        using var res = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);                                   // seule la TV est servie
    }

    // ---------- Écran « Partager vers la TV », avec un faux lecteur -----------------------------

    private sealed class FakePlayer : IMediaPlayer
    {
        public List<string> Calls { get; } = [];
        public PlaybackInfo Info { get; set; } = new("STOPPED", null, null);

        /// <summary>Faux : la TV télécharge le fichier dès l'envoi (sauf si <see cref="Blocked"/>).</summary>
        public bool Blocked { get; set; }
        public TimeSpan? Loaded { get; set; }
        public event Action<string>? RequestServed;
        public TimeSpan? BufferedUntil(TimeSpan position, TimeSpan? duration) => Loaded;

        public TimeSpan? LastDuration { get; private set; }

        public Task ShowAsync(string path, MediaType type, string title, TimeSpan? duration = null, CancellationToken ct = default)
        {
            LastDuration = duration;
            Calls.Add($"show {Path.GetFileName(path)} {type.Kind}");
            Info = new PlaybackInfo("TRANSITIONING", null, null);
            if (!Blocked) RequestServed?.Invoke("GET");
            return Task.CompletedTask;
        }
        public Task PlayAsync(CancellationToken ct = default) { Calls.Add("play"); return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken ct = default) { Calls.Add("pause"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct = default) { Calls.Add("stop"); return Task.CompletedTask; }
        public Task SeekAsync(TimeSpan position, CancellationToken ct = default) { Calls.Add($"seek {position}"); return Task.CompletedTask; }
        public Task<PlaybackInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(Info);
        public ValueTask DisposeAsync() { Calls.Add("dispose"); return ValueTask.CompletedTask; }
    }

    private (ShareViewModel Vm, FakePlayer Player, Func<DateTime, DateTime> Advance) NewVm()
    {
        var player = new FakePlayer();
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        var vm = new ShareViewModel(() => player, (p, _) => Task.FromResult(p.Replace(".heic", ".converti.jpg")), () => now,
            p => p.EndsWith(".jpg") ? throw new InvalidOperationException("pas de durée pour une photo") : TimeSpan.FromMinutes(56));
        return (vm, player, d => now = d);
    }

    [Fact]
    public async Task Liste_suivant_precedent_et_conversion_des_photos()
    {
        var (vm, player, _) = NewVm();
        var (added, ignored) = vm.Add([File("a.jpg"), File("b.heic"), File("c.mp4"), File("notes.txt"), Path.Combine(_dir, "absent.jpg")]);
        Assert.Equal((3, 2), (added, ignored));
        Assert.Equal((0, 0), vm.Add([Path.Combine(_dir, "a.jpg")]));                               // pas de doublon
        Assert.True(vm.HasItems);

        await vm.ShowSelectedAsync();                                                              // première sélection
        await vm.NextAsync();
        await vm.NextAsync();
        await vm.NextAsync();                                                                      // en boucle
        await vm.PreviousAsync();
        Assert.Equal(["show a.jpg Photo", "show b.converti.jpg Photo", "show c.mp4 Video", "show a.jpg Photo", "show c.mp4 Video"], player.Calls);
        Assert.True(vm.IsMedia);
        Assert.True(vm.Items[2].IsCurrent);
        Assert.False(vm.Items[0].IsCurrent);

        await vm.DisposeAsync();                                                                   // fermeture : arrêt sur la TV
        Assert.Equal(["stop", "dispose"], player.Calls[^2..]);
    }

    [Fact]
    public async Task Avance_recul_bornes_a_la_video()
    {
        var (vm, player, _) = NewVm();
        vm.Add([File("film.mkv")]);
        await vm.ShowSelectedAsync();
        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(100));
        await vm.SeekByAsync(-10);
        Assert.Equal("seek 00:00:00", player.Calls[^1]);
        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(100));
        await vm.SeekByAsync(30);
        Assert.Equal("seek 00:01:39", player.Calls[^1]);                                          // fin - 1 s
        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100));
        await vm.SeekByAsync(10);
        Assert.Equal("seek 00:00:50", player.Calls[^1]);
    }

    [Fact]
    public async Task Barre_de_progression_clic_glissement_et_suivi()
    {
        var (vm, player, advance) = NewVm();
        vm.Add([File("film.mp4")]);
        await vm.ShowSelectedAsync();
        var t = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        // La TV ne donne pas la durée (toujours 0) : celle lue par l'app est gardée et transmise à la TV.
        Assert.True(vm.CanSeekBar);
        Assert.Equal("56:00", vm.DurationText);
        Assert.Equal(TimeSpan.FromMinutes(56), player.LastDuration);

        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(12), null);
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.Equal((12d, 3360d, "0:12", "56:00"), (vm.PositionSeconds, vm.DurationSeconds, vm.ElapsedText, vm.DurationText));
        Assert.True(vm.CanSeekBar);

        // Glissement : la position de la TV n'écrase pas celle visée par l'utilisateur.
        vm.IsDragging = true;
        vm.PositionSeconds = 1800;
        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(14), TimeSpan.FromMinutes(56));
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.Equal(1800d, vm.PositionSeconds);
        Assert.Equal("30:00", vm.ElapsedText);
        vm.IsDragging = false;
        await vm.SeekToAsync(vm.PositionSeconds);
        Assert.Equal("seek 00:30:00", player.Calls[^1]);

        // La TV renvoie un instant l'ancienne position : ignorée 2 s, puis suivie de nouveau.
        advance(t = t.AddSeconds(1)); await vm.TickAsync();
        Assert.Equal(1800d, vm.PositionSeconds);
        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(1803), TimeSpan.FromMinutes(56));
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.Equal(1803d, vm.PositionSeconds);

        await vm.SeekToAsync(99999);                                                               // au-delà de la fin
        Assert.Equal("seek 00:55:59", player.Calls[^1]);

        // Partie chargée : jamais avant la position, jamais après la fin.
        player.Loaded = TimeSpan.FromSeconds(1900);
        advance(t = t.AddSeconds(3)); await vm.TickAsync();
        Assert.Equal(1900d, vm.BufferedSeconds);
        player.Loaded = TimeSpan.FromHours(5);
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.Equal(3360d, vm.BufferedSeconds);
    }

    [Fact]
    public void Plages_envoyees_et_partie_chargee()
    {
        var r = new ByteRanges();
        r.Add(0, 100);
        r.Add(300, 400);
        r.Add(100, 150);                                                                           // accolée : fusion
        Assert.Equal(150, r.ContiguousEnd(10));
        Assert.Equal(400, r.ContiguousEnd(350));
        Assert.Equal(200, r.ContiguousEnd(200));                                                   // trou : rien d'avance
        r.Add(140, 320);                                                                           // comble le trou
        Assert.Equal(400, r.ContiguousEnd(0));

        var file = new FileContent(File("v.mp4", 1000));                                           // estimation linéaire
        Assert.Equal(500, file.ByteAt(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100)));
        Assert.Equal(TimeSpan.FromSeconds(25), file.TimeAt(250, TimeSpan.FromSeconds(100)));
        Assert.Null(file.TimeAt(250, null));
    }

    [Fact]
    public async Task Fin_de_video_enchaine_mais_pas_apres_un_stop()
    {
        var (vm, player, advance) = NewVm();
        vm.Add([File("1.mp4"), File("2.mp4"), File("3.mp4")]);
        await vm.ShowSelectedAsync();
        var t = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.True(vm.IsPlaying);
        Assert.Equal("0:01 / 1:00", vm.PositionText);
        player.Info = new PlaybackInfo("STOPPED", null, null);                                     // fin naturelle
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.Equal("show 2.mp4 Video", player.Calls[^1]);

        player.Info = new PlaybackInfo("PLAYING", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        await vm.StopAsync();                                                                      // arrêt demandé
        player.Info = new PlaybackInfo("STOPPED", null, null);
        advance(t = t.AddSeconds(2)); await vm.TickAsync();
        Assert.Equal("stop", player.Calls[^1]);                                                    // pas d'enchaînement
        Assert.Equal("2.mp4", vm.Current!.Name);
    }

    [Fact]
    public async Task TV_qui_ne_telecharge_pas_pare_feu_signale()
    {
        var (vm, player, advance) = NewVm();
        player.Blocked = true;
        vm.Add([File("1.jpg")]);
        await vm.ShowSelectedAsync();
        var t = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        advance(t = t.AddSeconds(10)); await vm.TickAsync();
        Assert.False(vm.FirewallSuspected);
        advance(t = t.AddSeconds(11)); await vm.TickAsync();
        Assert.True(vm.FirewallSuspected);
        Assert.True(vm.LastFailed);
        Assert.Contains("pare-feu", vm.Status);

        var (ok, okPlayer, okAdvance) = NewVm();                                                   // TV qui télécharge : rien
        ok.Add([File("2.jpg")]);
        await ok.ShowSelectedAsync();
        okAdvance(t.AddSeconds(30)); await ok.TickAsync();
        Assert.False(ok.FirewallSuspected);
    }

    // ---------- Conversion des vidéos (TS, MKV…) -----------------------------------------------

    [Fact]
    public void Conversion_choix_des_pistes_et_des_codecs()
    {
        Assert.True(VideoRemuxer.NeedsRemux("film.ts"));
        Assert.True(VideoRemuxer.NeedsRemux("film.MKV"));
        Assert.True(VideoRemuxer.NeedsRemux("film.avi"));
        Assert.False(VideoRemuxer.NeedsRemux("film.mp4"));                                         // réorganisé à la volée
        Assert.False(VideoRemuxer.NeedsRemux("photo.jpg"));

        // Sortie réelle de ffprobe (MKV YouTube : VP9 + Opus), avec pochette et sous-titres.
        var probe = VideoRemuxer.ParseProbe(
            "index=0|codec_name=mjpeg|codec_type=video\r\n" +
            "index=1|codec_name=vp9|codec_type=video\r\n" +
            "index=2|codec_name=opus|codec_type=audio\r\n" +
            "index=3|codec_name=subrip|codec_type=subtitle\r\n");
        Assert.Equal(new VideoProbe("vp9", "opus"), probe);

        var vp9 = VideoRemuxer.BuildArguments("in.mkv", "out.mp4", probe);
        Assert.Equal(["-c:v", "copy"], vp9.Skip(vp9.IndexOf("-c:v")).Take(2));                    // image intacte
        Assert.Equal(["-c:a", "aac"], vp9.Skip(vp9.IndexOf("-c:a")).Take(2));                      // Opus → AAC
        Assert.Equal("out.mp4", vp9[^1]);
        Assert.Contains("0:a:0?", vp9);                                                            // son facultatif

        var ts = VideoRemuxer.BuildArguments("in.ts", "out.mp4", new VideoProbe("h264", "aac"));
        Assert.Equal(["-c:a", "copy"], ts.Skip(ts.IndexOf("-c:a")).Take(2));
        Assert.Contains("hvc1", VideoRemuxer.BuildArguments("in.mkv", "o.mp4", new VideoProbe("hevc", "ac3")));
        Assert.Throws<NotSupportedException>(() => VideoRemuxer.BuildArguments("in.mkv", "o.mp4", new VideoProbe("av1", "opus")));
        Assert.Throws<NotSupportedException>(() => VideoRemuxer.BuildArguments("in.mkv", "o.mp4", new VideoProbe(null, "mp3")));

        Assert.Equal(TimeSpan.FromSeconds(1.5), VideoRemuxer.ParseProgress("out_time_us=1500000"));
        Assert.Null(VideoRemuxer.ParseProgress("progress=continue"));
        Assert.Null(VideoRemuxer.ParseProgress("out_time_us=N/A"));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task Preparation_des_l_ajout_file_priorite_et_annulation()
    {
        var player = new FakePlayer();
        var gates = new Dictionary<string, TaskCompletionSource>();
        var order = new List<string>();
        var vm = new ShareViewModel(() => player, null, null, _ => TimeSpan.FromMinutes(2),
            async (path, duration, progress, ct) =>
            {
                var name = Path.GetFileName(path);
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gates) { order.Add(name); gates[name] = gate; }
                progress.Report(0.5);
                await gate.Task.WaitAsync(ct);
                return path + ".mp4";
            });
        void Release(string name) { lock (gates) gates[name].SetResult(); }
        bool Started(string name) { lock (gates) return gates.ContainsKey(name); }

        vm.Add([File("a.ts"), File("b.mkv"), File("c.mp4"), File("d.avi")]);
        await WaitFor(() => Started("a.ts"));
        Assert.Equal(PrepareState.Preparing, vm.Items[0].PrepareState);                           // dès l'ajout
        Assert.Equal(PrepareState.Queued, vm.Items[1].PrepareState);                               // une à la fois
        Assert.Equal(PrepareState.None, vm.Items[2].PrepareState);                                 // MP4 : rien à faire
        Assert.Equal("En attente de préparation pour la TV", vm.Items[1].PrepareText);
        await WaitFor(() => vm.Items[0].PrepareText == "Préparation pour la TV… 50 %");

        // Lecture de d.avi demandée pendant la préparation de a.ts : d.avi passe avant b.mkv.
        var show = vm.ShowAsync(vm.Items[3]);
        await WaitFor(() => vm.IsPreparing);
        Release("a.ts");
        await WaitFor(() => Started("d.avi"));
        Assert.Equal(["a.ts", "d.avi"], order);
        Release("d.avi");
        await show;
        Assert.False(vm.IsPreparing);
        Assert.Equal("show d.avi.mp4 Video", player.Calls[^1]);                                   // lecture automatique
        Assert.Equal("Prête pour la TV", vm.Items[3].PrepareText);

        await vm.ShowAsync(vm.Items[0]);                                                           // déjà prête : immédiat
        Assert.Equal("show a.ts.mp4 Video", player.Calls[^1]);

        // b.mkv : Stop arrête l'attente, pas la préparation ; la retirer de la liste l'annule.
        await WaitFor(() => Started("b.mkv"));
        var b = vm.Items[1];
        var wait = vm.ShowAsync(b);
        await WaitFor(() => vm.IsPreparing);
        await vm.StopAsync();
        await wait;
        Assert.Contains("continue en arrière-plan", vm.Status);
        Assert.Equal(PrepareState.Preparing, b.PrepareState);
        vm.Selected = b;
        vm.RemoveSelectedCommand.Execute(null);
        await WaitFor(() => b.PrepareState == PrepareState.None);
        Assert.Equal("show a.ts.mp4 Video", player.Calls[^1]);                                    // rien envoyé
    }

    [Fact]
    public async Task Preparation_impossible_affichee()
    {
        var player = new FakePlayer();
        var vm = new ShareViewModel(() => player, null, null, null,
            (_, _, _, _) => Task.FromException<string>(new NotSupportedException("format d'image « av1 » non lu par la TV.")));
        vm.Add([File("film.mkv")]);
        await WaitFor(() => vm.Items[0].PrepareState == PrepareState.Failed);
        Assert.Equal("Préparation impossible : format d'image « av1 » non lu par la TV", vm.Items[0].PrepareText);
        await vm.ShowAsync(vm.Items[0]);                                                           // nouvel essai, même échec
        Assert.True(vm.LastFailed);
        Assert.Contains("av1", vm.Status);
        Assert.Empty(player.Calls);
    }

    [Fact]
    public async Task Diaporama_au_rythme_choisi()
    {
        var (vm, player, advance) = NewVm();
        vm.Add([File("1.jpg"), File("2.jpg")]);
        vm.SlideshowSeconds = 5;
        vm.Slideshow = true;                                                                       // démarre sur la 1re photo
        await Task.Yield();
        Assert.Equal("show 1.jpg Photo", player.Calls[^1]);
        var t = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        player.Info = new PlaybackInfo("PLAYING", null, null);                                     // la TV affiche
        advance(t = t.AddSeconds(1)); await vm.TickAsync();
        advance(t = t.AddSeconds(3)); await vm.TickAsync();
        Assert.Equal("show 1.jpg Photo", player.Calls[^1]);                                       // pas encore
        advance(t = t.AddSeconds(3)); await vm.TickAsync();
        Assert.Equal("show 2.jpg Photo", player.Calls[^1]);                                       // 5 s écoulées

        vm.SlowerSlideshowCommand.Execute(null);
        Assert.Equal(8, vm.SlideshowSeconds);
        vm.FasterSlideshowCommand.Execute(null);
        vm.FasterSlideshowCommand.Execute(null);
        Assert.Equal(3, vm.SlideshowSeconds);
    }
}
