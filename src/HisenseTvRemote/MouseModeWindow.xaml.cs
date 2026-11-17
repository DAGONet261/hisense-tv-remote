using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using HisenseTvRemote.Core;
using HisenseTvRemote.Core.ViewModels;

namespace HisenseTvRemote;

/// <summary>
/// Mode souris plein écran : la souris du PC pilote la TV. Le curseur est caché et ramené à un point fixe
/// après chaque mouvement (comme dans un jeu), si bien que la souris ne bute jamais sur le bord de l'écran.
/// Les mouvements sont traduits en navigation par <see cref="PointerToKeys"/>.
/// </summary>
public partial class MouseModeWindow : Window
{
    private readonly RemoteViewModel _remote;
    private readonly Settings _settings;
    private readonly PointerToKeys _pointer;
    private POINT _anchor;
    private bool _anchored;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    private static readonly Dictionary<string, string> Arrows = new()
    {
        ["up"] = "▲ Haut", ["down"] = "▼ Bas", ["left"] = "◀ Gauche", ["right"] = "▶ Droite",
    };

    public MouseModeWindow(RemoteViewModel remote, Settings settings)
    {
        InitializeComponent();
        _remote = remote;
        _settings = settings;
        _pointer = new PointerToKeys(Math.Clamp(settings.MouseStepPixels, PointerToKeys.MinThreshold, PointerToKeys.MaxThreshold));
        ShowSensitivity();
        TvName.Text = "TV : " + (remote.CurrentTv ?? "aucune");
        remote.PropertyChanged += OnRemoteChanged;
        Closed += (_, _) => remote.PropertyChanged -= OnRemoteChanged;
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            // Point d'ancrage en pixels physiques (indépendant de la mise à l'échelle Windows).
            var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            Recenter((int)center.X, (int)center.Y);
            _anchored = true;
        };
        // Perdre le focus (Alt+Tab…) quitte le mode : la souris est rendue au PC.
        Deactivated += (_, _) => Leave();
        Closing += (_, _) => _closing = true;
        // La fenêtre principale reprend la main (sinon elle peut rester derrière d'autres fenêtres).
        Closed += (_, _) => Owner?.Activate();
    }

    /// <summary>
    /// Ramène le curseur au point d'ancrage et retient la position réellement atteinte (précaution : avec
    /// la mise à l'échelle Windows, elle pourrait différer d'un pixel et compter comme un mouvement).
    /// </summary>
    private void Recenter(int x, int y)
    {
        SetCursorPos(x, y);
        _anchor = GetCursorPos(out var landed) ? landed : new POINT { X = x, Y = y };
    }

    private void OnRemoteChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RemoteViewModel.Status)) Status.Text = _remote.Status;
    }

    private void Show(string action) => LastAction.Text = action;

    private bool _closing;

    /// <summary>
    /// Fermeture unique. Échap ferme la fenêtre, ce qui lui fait perdre le focus et redemandait une
    /// fermeture pendant la fermeture : WPF lève alors une exception qui arrêtait toute l'app (17/11).
    /// </summary>
    private void Leave()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void ShowSensitivity() =>
        Sensitivity.Text = $"Sensibilité : 1 pas tous les {_pointer.Threshold:0} px  (Maj + molette pour régler)";

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_anchored || !GetCursorPos(out var p)) return;
        var dx = p.X - _anchor.X;
        var dy = p.Y - _anchor.Y;
        if (dx == 0 && dy == 0) return;
        Recenter(_anchor.X, _anchor.Y);
        foreach (var key in _pointer.Add(dx, dy))
        {
            Show(Arrows[key]);
            _ = _remote.KeyCommand.ExecuteAsync(key);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        e.Handled = true;
        var (key, label) = e.ChangedButton switch
        {
            MouseButton.Left => ("ok", "● OK"),
            MouseButton.Right or MouseButton.XButton1 => ("returns", "↩ Retour"),
            MouseButton.Middle => ("home", "⌂ Accueil"),
            MouseButton.XButton2 => ("forwards", "⏩ Avance rapide"),
            _ => (null, null),
        };
        if (key is null) return;
        _pointer.Reset();
        Show(label!);
        _ = _remote.KeyCommand.ExecuteAsync(key);
    }

    protected override async void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Molette vers le haut = plus sensible (moins de distance par pas). Retenu pour la prochaine fois.
            _settings.MouseStepPixels = _pointer.Adjust(moreSensitive: e.Delta > 0);
            _settings.Save();
            ShowSensitivity();
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Show(e.Delta > 0 ? "🔊 Volume +" : "🔉 Volume −");
            await _remote.WheelVolumeAsync(e.Delta);
            return;
        }
        var key = e.Delta > 0 ? "up" : "down";
        Show(Arrows[key]);
        await _remote.KeyCommand.ExecuteAsync(key);
    }

    /// <summary>Échap quitte ; les autres raccourcis clavier restent actifs (sauf ceux qui ouvrent une fenêtre).</summary>
    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape) { e.Handled = true; Leave(); return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = KeyMods.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= KeyMods.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= KeyMods.Shift;
        if (KeyboardShortcuts.Match(key.ToString(), null, mods) is not { } shortcut) return;
        e.Handled = true;
        if (shortcut.Command == ShortcutCommand.OpenMouseMode) { Leave(); return; }   // F11 : bascule
        if (await _remote.RunShortcutAsync(shortcut)) Show(shortcut.Label);
    }
}
