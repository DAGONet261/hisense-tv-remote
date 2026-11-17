using System.Diagnostics;
using System.Text;

namespace HisenseTvRemote.Core.Vidaa;

/// <summary>
/// Appairage natif : connexion, demande d'affichage du PIN (vidaa_app_connect), attente du PIN saisi,
/// envoi, réception et enregistrement du jeton. Mêmes textes que <c>tv auth pair</c>.
/// </summary>
public sealed class NativePairingSession : IPairingSession
{
    private readonly NativeTv _owner;
    private readonly string? _alias;
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _err = new();
    private readonly TaskCompletionSource<CommandResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _pin = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTime _started = DateTime.Now;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private int _prompted;

    internal NativePairingSession(NativeTv owner, string? alias)
    {
        _owner = owner;
        _alias = alias;
    }

    public event Action<string>? OutputReceived;
    public event Action<CommandResult>? Completed;
    public event Action? PinRequested;

    public bool IsPinRequested => Volatile.Read(ref _prompted) == 1;
    public Task<CommandResult> Result => _done.Task;
    public string Output { get { lock (_out) return _out.ToString(); } }

    private List<string> Args => _alias is null ? ["auth", "pair"] : ["--tv", _alias, "auth", "pair"];

    public void Start() => _ = RunAsync();

    private void Write(string s)
    {
        lock (_out) _out.Append(s);
        OutputReceived?.Invoke(s);
    }

    private void Error(string s)
    {
        if (s.Length == 0) return;
        lock (_err) _err.AppendLine(s.TrimEnd());
        OutputReceived?.Invoke("\n" + s.TrimEnd());
    }

    private async Task RunAsync()
    {
        var code = 1;
        var acquired = false;
        try
        {
            var (client, tv, error) = await _owner.AcquireForPairingAsync(_alias, _cts.Token).ConfigureAwait(false);
            acquired = true;
            if (tv is not null) Write($"Starting pairing with {tv.Host}:{tv.Port} ...\n");
            if (client is null)
            {
                Error(error);
                if (tv is not null)
                    Error($"Failed to connect to {tv.Host}:{tv.Port}\nIf the TV is reachable but auth is refused (MQTT code 5), " +
                          "check that the TV's clock is correct (date/time, timezone and DST).");
                return;
            }

            {
                if (!await client.StartPairingAsync().ConfigureAwait(false)) { Error("Could not send the pairing request."); return; }
                Write("\nA PIN should appear on your TV screen.\n");
                Write("Enter the PIN (or 'q' to cancel): ");
                if (Interlocked.Exchange(ref _prompted, 1) == 0) PinRequested?.Invoke();

                var pin = await _pin.Task.WaitAsync(_cts.Token).ConfigureAwait(false);
                if (pin.Equals("q", StringComparison.OrdinalIgnoreCase))
                {
                    Write("Pairing cancelled.\n");
                    return;
                }
                var log = new StringBuilder();
                client.Log += Collect;
                try
                {
                    if (await client.AuthenticateAsync(pin).ConfigureAwait(false))
                    {
                        Write("Pairing successful! Credentials saved.\n");
                        code = 0;
                    }
                    else
                    {
                        Error(log.ToString());
                        Error("Pairing failed. Check the PIN and try again.");
                    }
                }
                finally { client.Log -= Collect; }

                void Collect(string m) => log.AppendLine(m);
            }
        }
        catch (OperationCanceledException)
        {
            Write("Pairing cancelled.\n");
        }
        catch (Exception ex)
        {
            Error($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (acquired) _owner.ReleasePairing();
            string o, e;
            lock (_out) o = _out.ToString();
            lock (_err) e = _err.ToString();
            var r = new CommandResult(NativeTv.Label, Args, code, o, e, _sw.Elapsed, _started);
            if (_done.TrySetResult(r)) Completed?.Invoke(r);
        }
    }

    public Task SubmitPinAsync(string pin)
    {
        _pin.TrySetResult(pin.Trim());
        return Task.CompletedTask;
    }

    public async Task CancelAsync(TimeSpan? grace = null)
    {
        if (_done.Task.IsCompleted) return;
        _pin.TrySetResult("q");
        try { await _done.Task.WaitAsync(grace ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { _cts.Cancel(); }
    }

    public void Dispose()
    {
        _pin.TrySetResult("q");
        _cts.Cancel();
        _cts.Dispose();
    }
}
