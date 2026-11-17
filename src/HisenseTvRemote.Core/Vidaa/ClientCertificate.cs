using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HisenseTvRemote.Core.Vidaa;

/// <summary>
/// Certificat client exigé par la TV (TLS mutuel), extrait de l'app officielle VIDAA et jamais livré avec
/// cette application. Rangé par poste et par compte dans %APPDATA%\HisenseTvRemote\certs
/// (vidaa_client.pem + vidaa_client.key). Import possible directement depuis le .p12 de l'APK.
/// </summary>
public static class ClientCertificate
{
    public const string CertFile = "vidaa_client.pem";
    public const string KeyFile = "vidaa_client.key";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HisenseTvRemote", "certs");

    /// <summary>Dossier réglé, sinon dossier par défaut ; null si la paire .pem/.key n'y est pas.</summary>
    public static (string Cert, string Key)? Find(string? configuredDirectory)
    {
        foreach (var dir in new[] { configuredDirectory, DefaultDirectory }.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var cert = Path.Combine(dir!, CertFile);
            var key = Path.Combine(dir!, KeyFile);
            if (File.Exists(cert) && File.Exists(key)) return (cert, key);
        }
        return null;
    }

    /// <summary>
    /// Charge la paire PEM. SChannel (Windows) refuse une clé éphémère issue d'un PEM : on repasse par
    /// PKCS#12 en mémoire.
    /// </summary>
    public static X509Certificate2 Load((string Cert, string Key) paths)
    {
        using var pem = X509Certificate2.CreateFromPemFile(paths.Cert, paths.Key);
        return new X509Certificate2(pem.Export(X509ContentType.Pkcs12));
    }

    /// <summary>
    /// Importe le conteneur .p12 de l'app VIDAA (res/raw/client_mobile_android.p12) protégé par
    /// <paramref name="password"/>, et l'écrit en PEM. Windows sait lire l'ancien chiffrement PKCS#12
    /// (RC2/3DES) de ce fichier : plus besoin d'OpenSSL ni de Python.
    /// </summary>
    /// <returns>Sujet du certificat importé.</returns>
    public static string ImportPkcs12(string p12Path, string password, string? targetDirectory = null)
    {
        using var cert = new X509Certificate2(p12Path, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        using var rsa = cert.GetRSAPrivateKey() ?? throw new CryptographicException("Le .p12 ne contient pas de clé privée RSA.");
        // L'export en clair d'une clé CNG est souvent refusé : export chiffré puis réimport, puis PEM.
        var tmp = Guid.NewGuid().ToString("N");
        var encrypted = rsa.ExportEncryptedPkcs8PrivateKey(tmp,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));
        using var plain = RSA.Create();
        plain.ImportEncryptedPkcs8PrivateKey(tmp, encrypted, out _);
        Write(targetDirectory, cert.ExportCertificatePem(), plain.ExportPkcs8PrivateKeyPem());
        return cert.Subject;
    }

    /// <summary>Importe une paire .pem/.key existante (vérifiée avant copie).</summary>
    public static string ImportPem(string certPath, string keyPath, string? targetDirectory = null)
    {
        using var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        Write(targetDirectory, File.ReadAllText(certPath), File.ReadAllText(keyPath));
        return cert.Subject;
    }

    private static void Write(string? targetDirectory, string certPem, string keyPem)
    {
        var dir = string.IsNullOrWhiteSpace(targetDirectory) ? DefaultDirectory : targetDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, CertFile), certPem);
        File.WriteAllText(Path.Combine(dir, KeyFile), keyPem);
    }
}
