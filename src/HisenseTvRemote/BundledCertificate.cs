using System.IO;
using System.Security.Cryptography;
using HisenseTvRemote.Core.Vidaa;

namespace HisenseTvRemote;

/// <summary>
/// Certificat client VIDAA intégré à l'application — **installeurs personnels seulement**. Intégré par la
/// compilation si <c>certs\vidaa_client.p12</c> (non versionné) et <c>VIDAA_P12_PASSWORD</c> (<c>.env</c>)
/// existent sur le poste ; jamais dans les installeurs publics (le workflow GitHub le vérifie). Installé
/// au démarrage s'il manque, ou s'il remplace un certificat plus ancien (nouvelle version de l'app Android).
/// </summary>
public static class BundledCertificate
{
    private const string P12Resource = "HisenseTvRemote.Bundled.vidaa_client.p12";
    private const string PasswordResource = "HisenseTvRemote.Bundled.vidaa_client.p12.pwd";

    public static bool IsPresent => typeof(BundledCertificate).Assembly.GetManifestResourceInfo(P12Resource) is not null;

    /// <summary>Message à afficher (installé, mis à jour ou inutilisable) ; null s'il n'y a rien à dire.</summary>
    public static (string Message, bool Failed)? EnsureInstalled(string? certDirectory)
    {
        if (!IsPresent) return null;
        try
        {
            var p12 = Read(P12Resource);
            var password = System.Text.Encoding.UTF8.GetString(Read(PasswordResource)).Trim();
            return ClientCertificate.InstallBundled(p12, password, certDirectory) switch
            {
                ClientCertificate.BundledResult.Installed => ("Certificat client VIDAA intégré installé.", false),
                ClientCertificate.BundledResult.Updated => ("Certificat client VIDAA mis à jour (version intégrée à l'application).", false),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return ("Certificat intégré inutilisable (mot de passe ?) : " + ex.Message, true);
        }
    }

    private static byte[] Read(string name)
    {
        using var s = typeof(BundledCertificate).Assembly.GetManifestResourceStream(name)
                      ?? throw new IOException($"ressource {name} absente");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
