using System.Windows;
using System.Windows.Media;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
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

    /// <summary>The status-bar label key, kept so it can be re-read on a language change.</summary>
    private string _statusKey = "status.notSignedIn";

    public MainWindow(IServiceProvider services, AgentSession session, Localizer localizer)
    {
        InitializeComponent();

        _services = services;
        _session = session;
        _localizer = localizer;

        // The window's own labels bind through the localizer.
        DataContext = this;

        VersionText.Text = $"v{LaptopInfo.AppVersion}";

        // The status bar is set from code rather than bound, so it has to be
        // told to re-read itself when the language changes.
        localizer.LanguageChanged += (_, _) => ConnectionStatusText.Text = localizer[_statusKey];

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

        // Amber rather than green while the phone is unconfigured: the agent is
        // signed in, but no call will arrive until the supervisor fills in the
        // extensions (A-01, A-02).
        if (_session.HasPhone)
        {
            SetConnectionStatus("status.signedIn", "#22C55E");
        }
        else
        {
            SetConnectionStatus("status.noExtensions", "#F59E0B");
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
