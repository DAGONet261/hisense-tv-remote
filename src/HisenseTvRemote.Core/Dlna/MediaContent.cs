using Microsoft.Win32.SafeHandles;

namespace HisenseTvRemote.Core.Dlna;

/// <summary>
/// Ce que le serveur envoie à la TV : un fichier tel quel, ou une vue réorganisée d'un fichier
/// (<see cref="InterleavedMp4"/>). Toujours lu dans le fichier d'origine <see cref="SourcePath"/>,
/// jamais recopié.
/// </summary>
public abstract class MediaContent(string sourcePath)
{
    public string SourcePath { get; } = sourcePath;

    /// <summary>Taille du contenu servi (peut différer de celle du fichier d'origine).</summary>
    public abstract long Length { get; }

    /// <summary>Lit à partir de <paramref name="position"/> du contenu servi ; 0 = fin.</summary>
    public abstract ValueTask<int> ReadAsync(SafeFileHandle source, long position, Memory<byte> buffer, CancellationToken ct);

    public SafeFileHandle OpenSource() =>
        File.OpenHandle(SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
}

/// <summary>Le fichier tel quel.</summary>
public sealed class FileContent(string path) : MediaContent(path)
{
    public override long Length { get; } = new FileInfo(path).Length;

    public override ValueTask<int> ReadAsync(SafeFileHandle source, long position, Memory<byte> buffer, CancellationToken ct) =>
        position >= Length ? ValueTask.FromResult(0) : RandomAccess.ReadAsync(source, buffer, position, ct);
}
