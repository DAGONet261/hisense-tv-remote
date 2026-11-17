using System.Text.Json.Nodes;
using HisenseTvRemote.Core;
using HisenseTvRemote.Core.Vidaa;

namespace HisenseTvRemote.Tests;

/// <summary>
/// Faux moteur TV en mémoire : enregistre les commandes reçues et répond comme le moteur natif
/// (« Sent: KEY_HOME », « Volume: 12 »…), sans réseau ni TV.
/// </summary>
public sealed class FakeEngine : ITvEngine
{
    private readonly Dictionary<string, IReadOnlyList<JsonObject>> _apps = [];

    public List<IReadOnlyList<string>> Received { get; } = [];
    public IReadOnlyList<DiscoveredTv> LastDiscovered { get; set; } = [];

    /// <summary>Apps renvoyées par « app list ».</summary>
    public List<JsonObject> InstalledApps { get; } =
    [
        new() { ["name"] = "Netflix", ["httpIcon"] = "data:image/png;base64,iconDownloadhttps://img.example/netflix.png" },
        new() { ["name"] = "VIDAA Art", ["httpIcon"] = "" },
    ];

    public Task<CommandResult?> TryRunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        Received.Add(args);
        var alias = args.Count > 1 && args[0] == "--tv" ? args[1] : null;
        var cmd = args.Skip(alias is null ? 0 : 2).ToList();
        (int Code, string Out, string Err) r = cmd switch
        {
            ["key", var k] => (0, $"Sent: KEY_{k.ToUpperInvariant()}", ""),
            ["volume", "get"] => (0, "Volume: 12", ""),
            ["volume", var d] => (0, $"Volume {d} x1", ""),
            ["app", "list"] => AppList(alias),
            ["status"] => (1, "", "Failed to connect to TV"),
            ["monitor"] => (-99, "", ""),
            _ => (0, "ok", ""),
        };
        if (r.Code == -99) return Task.FromResult<CommandResult?>(null);
        return Task.FromResult<CommandResult?>(new CommandResult(NativeTv.Label, args.ToList(), r.Code,
            r.Out.Length > 0 ? r.Out + "\n" : "", r.Err.Length > 0 ? r.Err + "\n" : "", TimeSpan.FromMilliseconds(1), DateTime.Now));
    }

    private (int, string, string) AppList(string? alias)
    {
        _apps[alias ?? ""] = InstalledApps;
        return (0, "Available apps:\n" + string.Join("\n", InstalledApps.Select(a => "  " + a["name"])), "");
    }

    public IPairingSession StartPairing(string? alias) => throw new NotSupportedException();
    public IReadOnlyList<JsonObject>? AppsOf(string? alias) => _apps.GetValueOrDefault(alias ?? "");
    public IReadOnlyList<JsonObject>? SourcesOf(string? alias) => null;
    public void Dispose() { }
}
