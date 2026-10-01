using System.Windows.Media;
using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.Shared.Contracts.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The signed-in shell: who is here, whether the phone works, how it should
/// treat an arriving call (A-18), the agent's break (A-86), and the way out.
/// </summary>
public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly SignInService _signIn;
    private readonly AgentSession _session;
    private readonly SipRegistrationService _sip;
    private readonly PhonePreferences _preferences;
    private readonly BreakService _breaks;

    /// <summary>Moves the break timer on once a second (A-86).</summary>
    private readonly DispatcherTimer _tick;

    public HomeViewModel(
        SignInService signIn,
        AgentSession session,
        SipRegistrationService sip,
        PhonePreferences preferences,
        BreakService breaks,
        Localizer localizer)
    {
        _signIn = signIn;
        _session = session;
        _sip = sip;
        _preferences = preferences;
        _breaks = breaks;
        Localizer = localizer;

        _breaks.Changed += OnBreakChanged;
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => RefreshBreak();
        _tick.Start();

        localizer.LanguageChanged += OnPhoneChanged;
        _sip.Changed += OnPhoneChanged;

        // The switches are also read from a SIP thread and could be set from
        // elsewhere later; the rail follows whatever the preferences say rather
        // than assuming it is the only thing that touches them.
        _preferences.Changed += OnPreferencesChanged;
    }

    private void OnPhoneChanged(object? sender, EventArgs e)
    {
        RefreshPhone();
        RefreshBreak();
    }

    private void OnBreakChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(OnBreak));
        OnPropertyChanged(nameof(DoNotDisturbEditable));
        OnPropertyChanged(nameof(AutoAnswerEditable));
        OnPropertyChanged(nameof(BreakButtonText));
        RefreshBreak();
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DoNotDisturb));
        OnPropertyChanged(nameof(AutoAnswer));
        OnPropertyChanged(nameof(AutoAnswerEditable));
    }

    /// <summary>
    /// Off the singletons at sign-out, with the shell's scope (M-A06). Each
    /// sign-in used to leave one more of these listening to the phone.
    /// </summary>
    public void Dispose()
    {
        Localizer.LanguageChanged -= OnPhoneChanged;
        _sip.Changed -= OnPhoneChanged;
        _preferences.Changed -= OnPreferencesChanged;
        _breaks.Changed -= OnBreakChanged;
        _tick.Stop();
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
    /// <remarks>
    /// The theme's own brushes, by name, rather than colours written out here
    /// (review, 27 Sep): a change to the palette now reaches the dot too.
    /// </remarks>
    public Brush StatusBrush => (Brush)System.Windows.Application.Current.FindResource(StatusColour);

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

    /// <summary>The theme brush for the dot, by its key in Theme.xaml.</summary>
    private string StatusColour
    {
        get
        {
            if (!_session.HasPhone)
            {
                return "Warning";
            }

            return _sip.State?.Status switch
            {
                RegistrationStatus.Registered => "Success",
                RegistrationStatus.Failed => "Danger",
                _ => "Warning",
            };
        }
    }

    /// <summary>
    /// Turn calls away without ringing (A-18). Kept as a pass-through rather
    /// than a copy: the call service reads the same object from a SIP thread,
    /// and two fields that could disagree about whether the phone is off is
    /// exactly the bug that would be blamed on the PBX.
    /// </summary>
    public bool DoNotDisturb
    {
        get => _preferences.DoNotDisturb;
        set
        {
            // A-86: on break, do not disturb stays on until Break out.
            if (_preferences.DoNotDisturb == value || (!value && _breaks.OnBreak))
            {
                OnPropertyChanged();
                return;
            }

            _preferences.DoNotDisturb = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Do not disturb can be switched by hand except during a break, which holds it on (A-86).</summary>
    public bool DoNotDisturbEditable => !_breaks.OnBreak;

    /// <summary>Auto answer means nothing while do not disturb is on (A-18).</summary>
    public bool AutoAnswerEditable => !DoNotDisturb;

    /// <summary>Pick a call up without pressing Answer (A-18).</summary>
    public bool AutoAnswer
    {
        get => _preferences.AutoAnswer;
        set
        {
            if (_preferences.AutoAnswer == value)
            {
                return;
            }

            _preferences.AutoAnswer = value;
            OnPropertyChanged();
        }
    }

    // ---- the break (A-86) ------------------------------------------------

    public bool OnBreak => _breaks.OnBreak;

    /// <summary>Break in, or Break out while on one.</summary>
    public string BreakButtonText => OnBreak ? Localizer["breaks.out"] : Localizer["breaks.in"];

    /// <summary>The day's break time, the break going included, counting on from the last.</summary>
    public string BreakTotalText => Localizer["breaks.today"].Replace("{time}", Clock(_breaks.TodayTotal()));

    /// <summary>The day's allowance, or nothing when the server could not say.</summary>
    public string BreakLimitText => _breaks.DailyLimitMinutes is { } limit
        ? Localizer["breaks.limit"].Replace("{minutes}", limit.ToString(System.Globalization.CultureInfo.InvariantCulture))
        : string.Empty;

    public bool IsOverLimit => _breaks.OverLimit() > TimeSpan.Zero;

    /// <summary>
    /// The warning past the limit (Dia, 1 Oct 2026). It does not stop Break in;
    /// it says, from the moment the day goes over, by how much.
    /// </summary>
    public string OverLimitText => IsOverLimit
        ? Localizer["breaks.overLimit"].Replace("{time}", Clock(_breaks.OverLimit()))
        : string.Empty;

    /// <summary>Since when, while on break.</summary>
    public string BreakSinceText => _breaks.BreakStartedAt is { } since
        ? Localizer["breaks.since"].Replace("{time}", since.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))
        : string.Empty;

    [RelayCommand]
    private async Task ToggleBreakAsync(CancellationToken ct)
    {
        if (_breaks.OnBreak)
        {
            await _breaks.BreakOutAsync(ct);
        }
        else
        {
            await _breaks.BreakInAsync(ct);
        }
    }

    private void RefreshBreak()
    {
        OnPropertyChanged(nameof(BreakTotalText));
        OnPropertyChanged(nameof(BreakLimitText));
        OnPropertyChanged(nameof(BreakSinceText));
        OnPropertyChanged(nameof(IsOverLimit));
        OnPropertyChanged(nameof(OverLimitText));
    }

    /// <summary>A length as h:mm:ss, in digits that read the same in both languages.</summary>
    private static string Clock(TimeSpan t) =>
        $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

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
