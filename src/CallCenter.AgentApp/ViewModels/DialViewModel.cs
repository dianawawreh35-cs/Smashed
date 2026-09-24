using System.Windows.Threading;
using CallCenter.AgentApp.Audio;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared.Phone;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The dial box: phoning a number that is not already on screen (A-20).
/// </summary>
/// <remarks>
/// Click-to-call from the call log and from a contact goes through
/// <see cref="DialAsync"/> here as well, so there is one route out to the
/// phone and one place that decides whether dialling is possible at all.
///
/// <see cref="CallService"/> raises its events on a SIP background thread, so
/// the one thing this class does with them is marshal to the dispatcher.
/// </remarks>
public partial class DialViewModel : ObservableObject
{
    private readonly CallService _calls;
    private readonly SipRegistrationService _sip;
    private readonly KeyTone _keyTone;
    private readonly Dispatcher _dispatcher;

    public DialViewModel(
        CallService calls,
        SipRegistrationService sip,
        KeyTone keyTone,
        Localizer localizer,
        Dispatcher dispatcher)
    {
        _calls = calls;
        _sip = sip;
        _keyTone = keyTone;
        _dispatcher = dispatcher;
        Localizer = localizer;

        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Hint));

        // Both change whether a call can be placed: the phone going away, and a
        // call starting or ending.
        _calls.StateChanged += (_, _) => _dispatcher.Invoke(RefreshCanDial);
        _sip.Changed += (_, _) => _dispatcher.Invoke(RefreshCanDial);
    }

    public Localizer Localizer { get; }

    /// <summary>What the agent typed. Anything but digits is ignored on dialling.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DialCommand))]
    private string _number = string.Empty;

    /// <summary>
    /// Why the Call button is dead, or nothing when it is not. A disabled button
    /// with no explanation is the worst version of this.
    /// </summary>
    public string Hint
    {
        get
        {
            if (!_sip.IsReady)
            {
                return Localizer["dial.phoneNotReady"];
            }

            return _calls.State.Status is not CallStatus.Idle
                ? Localizer["dial.callInProgress"]
                : string.Empty;
        }
    }

    /// <summary>
    /// Places the call. Also the way in for click-to-call, which passes the
    /// number rather than going through <see cref="Number"/>.
    /// </summary>
    public async Task DialAsync(string? number)
    {
        if (!CanDial(number))
        {
            return;
        }

        Number = PhoneNormalizer.DigitsOnly(number);
        await _calls.DialAsync(number);
    }

    [RelayCommand(CanExecute = nameof(CanDialTyped))]
    private Task Dial() => DialAsync(Number);

    /// <summary>A key on the pad (A-20). Appends to whatever is typed, with the key's beep.</summary>
    [RelayCommand]
    private void PressKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        Number += key;
        _keyTone.Play(key[0]);
    }

    /// <summary>The delete key: the last character goes.</summary>
    [RelayCommand]
    private void Backspace()
    {
        if (Number.Length > 0)
        {
            Number = Number[..^1];
        }
    }

    private bool CanDialTyped() => CanDial(Number);

    private bool CanDial(string? number) =>
        _sip.IsReady
        && _calls.State.Status is CallStatus.Idle
        && !string.IsNullOrEmpty(PhoneNormalizer.DigitsOnly(number));

    private void RefreshCanDial()
    {
        DialCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Hint));
    }
}
