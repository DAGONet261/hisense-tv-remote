using System.IO;
using System.Runtime.InteropServices;

namespace HisenseTvRemote;

/// <summary>
/// Durée d'une vidéo ou d'une musique, telle que l'Explorateur Windows l'affiche (propriété
/// <c>System.Media.Duration</c>, MP4, MKV, AVI, TS, WMV, MP3…). Nécessaire parce que la TV renvoie
/// toujours une durée nulle : sans elle, pas de barre de progression. À appeler sur le thread UI (STA).
/// </summary>
public static class MediaDuration
{
    public static TimeSpan? Of(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic? folder = shell.NameSpace(Path.GetDirectoryName(Path.GetFullPath(path)));
            dynamic? item = folder?.ParseName(Path.GetFileName(path));
            object? value = item?.ExtendedProperty("System.Media.Duration");
            if (value is null) return null;
            var d = TimeSpan.FromTicks(Convert.ToInt64(value));          // unités de 100 ns
            return d > TimeSpan.Zero ? d : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FormatException or OverflowException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException)
        {
            return null;
        }
    }
}
