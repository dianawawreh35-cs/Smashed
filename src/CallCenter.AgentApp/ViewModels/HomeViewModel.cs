using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared.Contracts.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// What the agent sees once signed in. A placeholder for now: it confirms who is
/// signed in and whether the phone is configured, and offers Log out (A-05).
/// The softphone, call log, contacts and app orders tabs replace its body.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly SignInService _signIn;
    private readonly AgentSession _session;
    private readonly SipRegistrationService _sip;

    public HomeViewModel(
        SignInService signIn, AgentSession session, SipRegistrationService sip, Localizer localizer)
    {
        _signIn = signIn;
        _session = session;
        _sip = sip;
        Localizer = localizer;

        localizer.LanguageChanged += (_, _) => RefreshPhone();
        _sip.Changed += (_, _) => RefreshPhone();
    }

    /// <summary>Re-reads everything that describes the phone (A-02).</summary>
    private void RefreshPhone()
    {
        OnPropertyChanged(nameof(PhoneSummary));
        OnPropertyChanged(nameof(CustomerStatus));
        OnPropertyChanged(nameof(InternalStatus));
    }

    /// <summary>The customer extension's registration, in the agent's language.</summary>
    public string CustomerStatus => Describe(_sip.Customer);

    /// <summary>The internal extension's registration, in the agent's language.</summary>
    public string InternalStatus => Describe(_sip.Internal);

    /// <summary>
    /// A line the agent can act on: the state, and the PBX's own words when it
    /// refused, so a supervisor has something to work from.
    /// </summary>
    private string Describe(RegistrationState? state)
    {
        if (state is null)
        {
            return Localizer["sip.idle"];
        }

        var status = Localizer[state.Status switch
        {
            RegistrationStatus.Registering => "sip.registering",
            RegistrationStatus.Registered => "sip.registered",
            RegistrationStatus.Failed => "sip.failed",
            RegistrationStatus.Retrying => "sip.retrying",
            _ => "sip.idle",
        }];

        return state.Detail is null ? status : $"{status} — {state.Detail}";
    }

    /// <summary>Bound by the view for its labels and the language toggle.</summary>
    public Localizer Localizer { get; }

    /// <summary>Raised after signing out, so the shell goes back to the login screen.</summary>
    public event EventHandler? SignedOut;

    public string DisplayName => _session.User?.DisplayName ?? string.Empty;

    /// <summary>
    /// The two extensions the app will register (A-02), once SIP is wired up.
    /// Labelled, because which is which is not obvious from the numbers alone.
    /// </summary>
    public string PhoneSummary => _session.Extensions is { } extensions
        ? $"{Localizer["home.customerExtension"]} {extensions.CustomerExtension}  ·  "
          + $"{Localizer["home.internalExtension"]} {extensions.InternalExtension}  ·  {extensions.SipServer}"
        : Localizer["home.noExtensions"];

    public bool HasPhone => _session.HasPhone;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private void ToggleLanguage() => Localizer.Toggle();

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken ct)
    {
        IsBusy = true;

        try
        {
            await _signIn.SignOutAsync(LogoutReasons.Manual, ct);
            SignedOut?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
