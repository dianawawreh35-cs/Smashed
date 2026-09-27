using System.Windows;
using System.Windows.Input;
using CallCenter.AgentApp.ViewModels;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// The call's controls from the keyboard (M-A05, A-12): Answer, Reject, Hang
/// up, Mute and Hold.
/// </summary>
/// <remarks>
/// <b>In the app only</b>, on the main window and the pop-up, never global
/// (Dia, 27 Sep): a system-wide hotkey would clash with the POS and the
/// browser, which the agents have open all day.
///
/// <b>Ctrl+Shift and a letter</b>, so no key an agent types into a box can set
/// one off: not a digit, not Enter, not a letter on its own. The letter is
/// the English word's first (A Answer, R Reject, E End, M Mute, H Hold), read
/// from the key's position, so it is the same key with the keyboard on Arabic.
/// Each command does nothing in a state it does not belong to: Reject only
/// while ringing, Hang up only on a call that is up or being dialled, which
/// <see cref="Services.Calls.CallService"/> checks itself.
/// </remarks>
public static class CallShortcuts
{
    private const ModifierKeys Modifiers = ModifierKeys.Control | ModifierKeys.Shift;

    /// <summary>The keys, as the pop-up's buttons show them in their tooltips.</summary>
    public const string Answer = "Ctrl+Shift+A";
    public const string Reject = "Ctrl+Shift+R";
    public const string HangUp = "Ctrl+Shift+E";
    public const string Mute = "Ctrl+Shift+M";
    public const string Hold = "Ctrl+Shift+H";

    public static void Attach(Window window, CallViewModel call)
    {
        window.InputBindings.Add(new KeyBinding(call.AnswerCommand, Key.A, Modifiers));
        window.InputBindings.Add(new KeyBinding(call.RejectCommand, Key.R, Modifiers));
        window.InputBindings.Add(new KeyBinding(call.HangUpCommand, Key.E, Modifiers));
        window.InputBindings.Add(new KeyBinding(call.ToggleMuteCommand, Key.M, Modifiers));
        window.InputBindings.Add(new KeyBinding(call.ToggleHoldCommand, Key.H, Modifiers));
    }
}
