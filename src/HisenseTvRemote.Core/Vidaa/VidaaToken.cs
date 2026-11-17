namespace HisenseTvRemote.Core.Vidaa;

/// <summary>Jetons d'appairage d'une TV (accès + rafraîchissement), tels que stockés dans la base.</summary>
public sealed record VidaaToken(
    string Key, string? AccessToken, string? RefreshToken, double? AccessExpiresAt, double? RefreshExpiresAt,
    string? ClientId, string? MqttUsername, string? Uuid, string? Host, int Port, string? AuthMethod, int? ProtocolVersion)
{
    private const int ExpiryBufferSeconds = 300;

    public bool IsTokenless => AuthMethod == "static";
    public bool AccessValid => IsTokenless || !Expired(AccessExpiresAt);
    public bool RefreshValid => !IsTokenless && !Expired(RefreshExpiresAt);
    public bool NeedsRefresh => !AccessValid && RefreshValid;
    public bool NeedsReauth => !AccessValid && !RefreshValid;

    public long AccessExpiresIn => Remaining(AccessExpiresAt);
    public long RefreshExpiresIn => Remaining(RefreshExpiresAt);

    private static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    private static bool Expired(double? at) => at is null || Now > at - ExpiryBufferSeconds;
    private static long Remaining(double? at) => at is null ? 0 : Math.Max(0, (long)(at.Value - Now));
}
