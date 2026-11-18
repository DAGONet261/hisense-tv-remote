using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace HisenseTvRemote;

/// <summary>
/// Règle de pare-feu du partage vers la TV : la TV doit pouvoir télécharger les fichiers servis par l'app.
/// Sur certains postes, Windows bloque sans rien demander (notification « nouvelle app bloquée »
/// désactivée) : l'app vérifie donc la règle et propose de la créer (confirmation administrateur).
/// Même règle que l'installeur : programme, TCP entrant, réseau privé, sous-réseau local.
/// </summary>
public static class FirewallHelper
{
    public const string RuleName = "VIDAA - Hisense (partage vers la TV)";
    private const int Inbound = 1, Allow = 1, PrivateProfile = 2, PublicProfile = 4;

    /// <summary>
    /// Une règle entrante active autorise-t-elle <paramref name="exe"/> sur le réseau privé ?
    /// Null si on ne peut pas le savoir (pare-feu tiers, API indisponible).
    /// </summary>
    public static bool? HasAllowRule(string exe)
    {
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return null;
            dynamic policy = Activator.CreateInstance(type)!;
            if (!(bool)policy.FirewallEnabled[PrivateProfile]) return true;          // pare-feu privé désactivé
            var full = Path.GetFullPath(exe);
            foreach (dynamic rule in policy.Rules)
            {
                string? app = rule.ApplicationName;
                if (app is null || !string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(app)), full, StringComparison.OrdinalIgnoreCase))
                    continue;
                if ((bool)rule.Enabled && (int)rule.Direction == Inbound && (int)rule.Action == Allow && ((int)rule.Profiles & PrivateProfile) != 0)
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or NotSupportedException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Réseau actuel classé « Public » (et pas « Privé ») : Windows y bloque le partage, quelle que soit la
    /// règle (prévue pour le réseau privé). Null si on ne peut pas le savoir.
    /// </summary>
    public static bool? IsPublicNetworkOnly()
    {
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return null;
            dynamic policy = Activator.CreateInstance(type)!;
            var types = (int)policy.CurrentProfileTypes;
            return (types & PublicProfile) != 0 && (types & PrivateProfile) == 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Crée (ou recrée) la règle pour <paramref name="exe"/>, avec confirmation administrateur (UAC).
    /// Renvoie null si c'est fait, sinon la raison de l'échec.
    /// </summary>
    public static string? AddRule(string exe)
    {
        var name = $"name=\"{RuleName}\" program=\"{exe}\"";
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c netsh advfirewall firewall delete rule {name} >nul 2>&1 & " +
                        $"netsh advfirewall firewall add rule {name} dir=in action=allow enable=yes profile=private remoteip=localsubnet protocol=TCP",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "Windows n'a pas lancé la commande.";
            p.WaitForExit(30_000);
            return p.HasExited && p.ExitCode == 0 ? null : $"la commande du pare-feu a échoué (code {(p.HasExited ? p.ExitCode : -1)}).";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "autorisation administrateur refusée.";
        }
        catch (Win32Exception ex)
        {
            return ex.Message;
        }
    }
}
