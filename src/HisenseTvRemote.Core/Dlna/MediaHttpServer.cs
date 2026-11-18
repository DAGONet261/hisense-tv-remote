using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>
/// Petit serveur HTTP qui met des fichiers locaux à disposition de la TV (DLNA : la TV vient elle-même
/// chercher le fichier à l'adresse qu'on lui donne). Gère GET, HEAD et les plages d'octets (avance/recul
/// dans une vidéo).
/// Sécurité : seuls les fichiers publiés sont servis, chacun sous un jeton aléatoire, et seulement aux
/// adresses autorisées (la TV ; le PC lui-même pour les tests). Écoute sur toutes les interfaces : Windows
/// demande l'autorisation du pare-feu au premier partage (l'installeur ajoute une règle « réseau privé »).
/// </summary>
public sealed class MediaHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IReadOnlySet<IPAddress> _allowed;
    private readonly ConcurrentDictionary<string, (string Path, MediaType Type)> _files = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <param name="advertisedHost">Adresse du PC vue par la TV (pour construire les liens).</param>
    /// <param name="allowedClients">Seules ces adresses peuvent télécharger (les autres reçoivent 403).</param>
    /// <param name="port">0 : port libre choisi par Windows.</param>
    public MediaHttpServer(IPAddress advertisedHost, IEnumerable<IPAddress> allowedClients, int port = 0, IPAddress? bind = null)
    {
        _allowed = new HashSet<IPAddress>(allowedClients.Select(a => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a));
        _listener = new TcpListener(bind ?? IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://{advertisedHost}:{Port}";
        _loop = AcceptLoopAsync(_stop.Token);
    }

    public int Port { get; }
    public string BaseUrl { get; }

    /// <summary>Fichier servi à un appareil autorisé (méthode, fichier, statut 200/206).</summary>
    public event Action<string>? RequestServed;

    /// <summary>Demande refusée (appareil non autorisé, fichier inconnu) : diagnostic.</summary>
    public event Action<string>? RequestRejected;

    /// <summary>Publie un fichier et renvoie son adresse. Le nom du fichier n'apparaît pas dans l'adresse.</summary>
    public string Publish(string path, MediaType type)
    {
        var token = Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant();
        _files[token] = (path, type);
        return $"{BaseUrl}/media/{token}";
    }

    public void Unpublish(string url)
    {
        var i = url.LastIndexOf('/');
        if (i >= 0) _files.TryRemove(url[(i + 1)..], out _);
    }

    /// <summary>Adresse IPv4 du PC sur le chemin vers <paramref name="target"/> (aucun paquet n'est envoyé).</summary>
    public static IPAddress LocalAddressFor(IPAddress target)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Connect(target, 9);
        return ((IPEndPoint)s.LocalEndPoint!).Address;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (ct.IsCancellationRequested) return; continue; }
            _ = Task.Run(() => HandleAsync(client, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();

            var head = await ReadHeadAsync(stream, ct).ConfigureAwait(false);
            if (head is null) return;
            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length < 2) { await SendStatusAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return; }
            var method = parts[0];
            var target = parts[1];
            string? range = null;
            foreach (var line in lines.Skip(1))
            {
                var c = line.IndexOf(':');
                if (c > 0 && line[..c].Trim().Equals("Range", StringComparison.OrdinalIgnoreCase)) range = line[(c + 1)..].Trim();
            }

            if (!_allowed.Contains(remote))
            {
                await SendStatusAsync(stream, 403, "Forbidden", ct).ConfigureAwait(false);
                RequestRejected?.Invoke($"{method} refusé à {remote} (403)");
                return;
            }
            if (method is not ("GET" or "HEAD"))
            {
                await SendStatusAsync(stream, 405, "Method Not Allowed", ct).ConfigureAwait(false);
                return;
            }
            var q = target.IndexOf('?');
            var pathPart = q >= 0 ? target[..q] : target;
            var token = pathPart.StartsWith("/media/", StringComparison.Ordinal) ? pathPart[7..] : "";
            if (!_files.TryGetValue(token, out var file) || !File.Exists(file.Path))
            {
                await SendStatusAsync(stream, 404, "Not Found", ct).ConfigureAwait(false);
                RequestRejected?.Invoke($"{method} {pathPart} (404)");
                return;
            }

            await using var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, useAsync: true);
            var size = fs.Length;
            var r = ByteRange.Parse(range, size);
            if (r is { IsUnsatisfiable: true })
            {
                var h416 = $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{size}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(h416), ct).ConfigureAwait(false);
                return;
            }
            var start = r?.Start ?? 0;
            var length = r?.Length ?? size;
            var sb = new StringBuilder();
            sb.Append(r is null ? "HTTP/1.1 200 OK\r\n" : "HTTP/1.1 206 Partial Content\r\n");
            sb.Append("Content-Type: ").Append(file.Type.Mime).Append("\r\n");
            sb.Append("Content-Length: ").Append(length).Append("\r\n");
            if (r is { } rr) sb.Append($"Content-Range: bytes {rr.Start}-{rr.End}/{size}\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            sb.Append("transferMode.dlna.org: ").Append(file.Type.Kind == MediaKind.Photo ? "Interactive" : "Streaming").Append("\r\n");
            sb.Append("contentFeatures.dlna.org: ").Append(file.Type.ContentFeatures).Append("\r\n");
            sb.Append("Server: Windows UPnP/1.0 DLNADOC/1.50 VIDAA-Hisense/1.0\r\n");
            sb.Append("Connection: close\r\n\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
            RequestServed?.Invoke($"{method} {Path.GetFileName(file.Path)} {(r is null ? "200" : $"206 {start}+{length}")}");
            if (method == "HEAD") return;

            fs.Position = start;
            var buffer = new byte[128 * 1024];
            var left = length;
            while (left > 0)
            {
                var n = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct).ConfigureAwait(false);
                if (n == 0) break;
                await stream.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                left -= n;
            }
        }
        // La TV coupe souvent la connexion d'elle-même (changement de position, fin de lecture) : normal.
        catch (IOException) { }
        catch (SocketException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Lit l'en-tête HTTP (jusqu'à la ligne vide), 16 Kio au plus.</summary>
    private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var count = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (count < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(count, 1), timeout.Token).ConfigureAwait(false);
            if (n == 0) return null;
            count++;
            if (count >= 4 && buffer[count - 4] == '\r' && buffer[count - 3] == '\n' && buffer[count - 2] == '\r' && buffer[count - 1] == '\n')
                return Encoding.ASCII.GetString(buffer, 0, count - 4);
        }
        return null;
    }

    private static Task SendStatusAsync(NetworkStream stream, int code, string reason, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct).AsTask();

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _loop.ConfigureAwait(false); } catch (Exception) { /* arrêt */ }
        _stop.Dispose();
    }
}
