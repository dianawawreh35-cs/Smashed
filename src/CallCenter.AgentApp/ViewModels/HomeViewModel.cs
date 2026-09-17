using System.Windows.Media;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared.Contracts.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The signed-in shell: who is here, whether the phone works, and the way out.
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

    public Localizer Localizer { get; }

    /// <summary>Raised after signing out, so the shell goes back to the login screen.</summary>
    public event EventHandler? SignedOut;

    public string DisplayName => _session.User?.DisplayName ?? string.Empty;

    /// <summary>The initial shown in the rail's avatar.</summary>
    public string Initial => DisplayName.Trim() is { Length: > 0 } name
        ? name[..1].ToUpperInvariant()
        : "?";

    /// <summary>
    /// The phone in one line: where it stands, then the extension and host so a
    /// supervisor can check the settings without leaving the screen.
    /// </summary>
    public string PhoneSummary => _session.Extensions is { } extensions
        ? $"{extensions.Extension} · {extensions.SipServer}"
        : Localizer["home.noExtension"];

    /// <summary>The headline in the rail's status card (A-02).</summary>
    public string StatusTitle => Localizer[StatusKey];

    /// <summary>
    /// The dot beside it. Green only when calls can actually arrive; red when
    /// the PBX refused, because that needs a supervisor rather than patience.
    /// </summary>
    public Brush StatusBrush => new SolidColorBrush(StatusColour);

    private string StatusKey
    {
        get
        {
            if (!_session.HasPhone)
            {
                return "status.noExtension";
            }

            return _sip.State?.Status switch
            {
                RegistrationStatus.Registered => "status.registered",
                RegistrationStatus.Failed => "status.registrationFailed",
                RegistrationStatus.Retrying => "status.retrying",
                _ => "status.registering",
            };
        }
    }

    private Color StatusColour
    {
        get
        {
            if (!_session.HasPhone)
            {
                return Color.FromRgb(0xD2, 0x99, 0x22);
            }

            return _sip.State?.Status switch
            {
                RegistrationStatus.Registered => Color.FromRgb(0x3F, 0xB9, 0x50),
                RegistrationStatus.Failed => Color.FromRgb(0xF8, 0x51, 0x49),
                _ => Color.FromRgb(0xD2, 0x99, 0x22),
            };
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    private void RefreshPhone()
    {
        OnPropertyChanged(nameof(PhoneSummary));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusBrush));
    }

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
