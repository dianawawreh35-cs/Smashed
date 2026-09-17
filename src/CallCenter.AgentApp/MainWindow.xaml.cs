using System.Windows;
using System.Windows.Media;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.AgentApp.ViewModels;
using CallCenter.AgentApp.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.AgentApp;

/// <summary>
/// Shell window. Shows the login screen until an agent signs in, then the
/// signed-in view; the softphone, screen pop and history views are added with
/// their features.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly AgentSession _session;
    private readonly Localizer _localizer;
    private readonly SipRegistrationService _sip;

    /// <summary>The status-bar label key, kept so it can be re-read on a language change.</summary>
    private string _statusKey = "status.notSignedIn";

    public MainWindow(
        IServiceProvider services, AgentSession session, Localizer localizer, SipRegistrationService sip)
    {
        InitializeComponent();

        _services = services;
        _session = session;
        _localizer = localizer;
        _sip = sip;

        // The window's own labels bind through the localizer.
        DataContext = this;

        VersionText.Text = $"v{LaptopInfo.AppVersion}";

        // The status bar is set from code rather than bound, so it has to be
        // told to re-read itself when the language changes.
        localizer.LanguageChanged += (_, _) => ConnectionStatusText.Text = localizer[_statusKey];

        // Registration happens on background threads, so hop to the UI thread.
        sip.Changed += (_, _) => Dispatcher.Invoke(RefreshPhoneStatus);

        ShowLogin();
    }

    /// <summary>Bound by the window's XAML for its title and labels.</summary>
    public Localizer Localizer => _localizer;

    /// <summary>Puts the login screen in the shell and clears the status bar.</summary>
    private void ShowLogin()
    {
        var viewModel = _services.GetRequiredService<LoginViewModel>();
        viewModel.SignedIn += (_, _) => ShowHome();

        ShellContent.Content = new LoginView(viewModel);

        SignedInAsText.Visibility = Visibility.Collapsed;
        SetConnectionStatus("status.notSignedIn", "#EF4444");
    }

    /// <summary>Puts the signed-in view in the shell.</summary>
    private void ShowHome()
    {
        var viewModel = _services.GetRequiredService<HomeViewModel>();
        viewModel.SignedOut += (_, _) => ShowLogin();

        ShellContent.Content = new HomeView(viewModel);

        SignedInAsText.Text = _session.User?.DisplayName ?? string.Empty;
        SignedInAsText.Visibility = Visibility.Visible;

        RefreshPhoneStatus();
    }

    /// <summary>
    /// The status bar, from the registration state (A-02). The colour says
    /// whether calls can arrive; the text says what to do if they cannot.
    /// </summary>
    private void RefreshPhoneStatus()
    {
        if (!_session.IsSignedIn)
        {
            SetConnectionStatus("status.notSignedIn", "#EF4444");
            return;
        }

        // Signed in, but the supervisor has not assigned an extension yet. The
        // agent can still use contacts and app orders, so amber, not red.
        if (!_session.HasPhone)
        {
            SetConnectionStatus("status.noExtension", "#F59E0B");
            return;
        }

        switch (_sip.State?.Status ?? RegistrationStatus.Idle)
        {
            // A refusal will not fix itself: the agent needs their supervisor
            // rather than to keep waiting.
            case RegistrationStatus.Failed:
                SetConnectionStatus("status.registrationFailed", "#EF4444");
                break;

            case RegistrationStatus.Registered:
                SetConnectionStatus("status.registered", "#22C55E");
                break;

            case RegistrationStatus.Retrying:
                SetConnectionStatus("status.retrying", "#F59E0B");
                break;

            default:
                SetConnectionStatus("status.registering", "#F59E0B");
                break;
        }
    }

    /// <summary>Updates the status bar from a label key, and its indicator colour.</summary>
    private void SetConnectionStatus(string key, string colour)
    {
        _statusKey = key;

        ConnectionStatusText.Text = _localizer[key];
        ConnectionStatusLight.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(colour));
    }
}
