using HisenseTvRemote.Core;

namespace HisenseTvRemote;

/// <summary>Ligne du journal : commande exacte, code de sortie, stdout/stderr.</summary>
public sealed class LogEntry
{
    public LogEntry(CommandResult r) => Result = r;

    public CommandResult Result { get; }
    public string Time => Result.StartedAt.ToString("HH:mm:ss");
    public string ExitCode => Result.TimedOut ? "timeout" : Result.StartFailed ? "échec" : Result.ExitCode.ToString();
    public string CommandLine => Result.CommandLine;

    public string Detail =>
        $"Commande : {Result.CommandLine}\r\nCode de sortie : {ExitCode}  ({Result.Duration.TotalSeconds:0.0} s)\r\n" +
        $"--- stdout ---\r\n{Result.StandardOutput}\r\n--- stderr ---\r\n{Result.StandardError}";
}
