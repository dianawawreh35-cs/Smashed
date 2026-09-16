using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>The sign-in screen (A-01). Username and password only — never SIP details.</summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly SignInService _signIn;

    public LoginViewModel(SignInService signIn, Localizer localizer)
    {
        _signIn = signIn;
        Localizer = localizer;

        Login = signIn.LastLogin ?? string.Empty;

        // The failure is kept as a code, so the message follows the agent from
        // one language to the other while it is still on screen (A-80).
        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(ErrorMessage));
    }

    /// <summary>Bound by the view for its labels and the language toggle.</summary>
    public Localizer Localizer { get; }

    /// <summary>Raised once the agent is signed in, so the shell can show the main view.</summary>
    public event EventHandler? SignedIn;

    /// <summary>
    /// Pre-filled with the last username used on this laptop. The password never
    /// is — every agent types their own.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _login = string.Empty;

    /// <summary>
    /// Set by the view's PasswordBox. Not an <c>ObservableProperty</c>: a
    /// password should not sit in a binding engine or a change notification.
    /// </summary>
    public string Password { private get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    private string? _errorCode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorCode);

    /// <summary>The failure, in the language the agent is using.</summary>
    public string? ErrorMessage =>
        ErrorCode is null ? null : Localizer[$"login.errors.{ErrorCode}"];

    /// <summary>True when the username box has been filled in and no call is in flight.</summary>
    public bool CanSignIn => !IsBusy && !string.IsNullOrWhiteSpace(Login);

    /// <summary>The username box is pre-filled, so put the caret in the password box.</summary>
    public bool StartInPasswordBox => !string.IsNullOrWhiteSpace(Login);

    [RelayCommand]
    private void ToggleLanguage() => Localizer.Toggle();

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken ct)
    {
        IsBusy = true;
        ErrorCode = null;

        try
        {
            var result = await _signIn.SignInAsync(Login, Password, ct);

            if (!result.Succeeded)
            {
                ErrorCode = result.ErrorCode;
                return;
            }

            // Cleared on the way out: nothing keeps the password around once it
            // has been used, not even for the length of the shift.
            Password = string.Empty;
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
