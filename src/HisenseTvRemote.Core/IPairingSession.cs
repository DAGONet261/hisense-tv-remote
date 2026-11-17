namespace HisenseTvRemote.Core;

/// <summary>
/// Une session d'appairage par code PIN (<see cref="Vidaa.NativePairingSession"/> ; un faux dans les tests).
/// Le dialogue de l'app ne dépend que de cette interface.
/// </summary>
public interface IPairingSession : IDisposable
{
    /// <summary>Texte de progression, au fil de l'eau (mêmes messages que <c>tv auth pair</c>).</summary>
    event Action<string>? OutputReceived;
    event Action<CommandResult>? Completed;
    /// <summary>La TV affiche le PIN : la saisie peut commencer.</summary>
    event Action? PinRequested;

    bool IsPinRequested { get; }
    Task<CommandResult> Result { get; }
    string Output { get; }

    void Start();
    Task SubmitPinAsync(string pin);
    /// <summary>Annule proprement (équivalent de répondre « q »).</summary>
    Task CancelAsync(TimeSpan? grace = null);
}
