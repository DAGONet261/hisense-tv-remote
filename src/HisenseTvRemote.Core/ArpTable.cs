using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HisenseTvRemote.Core;

/// <summary>
/// Retrouve l'IP actuelle d'une TV à partir de sa MAC (enregistrée dans la base) via la table ARP
/// de Windows. Repli quand le bail DHCP de la TV change et que la recherche UPnP ne donne pas sa MAC.
/// La table n'est à jour qu'après un échange réseau avec la TV (ex. <c>tv discover</c>).
/// </summary>
public static partial class ArpTable
{
    /// <summary>Format de <c>arp -a</c> : « 192.168.88.19   e4-3b-c9-f9-5c-7e   dynamique ».</summary>
    public static string? FindIp(string arpOutput, string mac)
    {
        var wanted = NormalizeMac(mac);
        foreach (Match m in EntryRegex().Matches(arpOutput))
            if (NormalizeMac(m.Groups["mac"].Value) == wanted)
                return m.Groups["ip"].Value;
        return null;
    }

    public static async Task<string?> FindIpAsync(string mac, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("arp", "-a")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return FindIp(output, mac);
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static string NormalizeMac(string mac) =>
        new string(mac.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();

    [GeneratedRegex(@"(?<ip>(?:\d{1,3}\.){3}\d{1,3})\s+(?<mac>[0-9a-fA-F]{2}(?:[-:][0-9a-fA-F]{2}){5})")]
    private static partial Regex EntryRegex();
}
