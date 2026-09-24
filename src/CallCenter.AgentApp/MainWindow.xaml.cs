using System.Windows;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.ViewModels;
using CallCenter.AgentApp.Views;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.AgentApp;

/// <summary>
/// The window. A frame around two states: signed out, and signed in.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    public MainWindow(IServiceProvider services, Localizer localizer)
    {
        InitializeComponent();

        _services = services;
        Localizer = localizer;

        DataContext = this;

        // The server can end the sign-in from its side (N-05). Back to the
        // sign-in screen, saying why, rather than a main screen that fails at
        // every turn.
        services.GetRequiredService<SignedOutByServer>().SignedOut += (_, _) =>
            Dispatcher.Invoke(() => ShowLogin(LoginErrorCodes.SignedOut));

        ShowLogin();
    }

    /// <summary>Bound by the window's XAML for its title and direction.</summary>
    public Localizer Localizer { get; }

    /// <param name="reason">
    /// One of <see cref="LoginErrorCodes"/>, shown under the password box, when
    /// the agent did not sign out themselves.
    /// </param>
    private void ShowLogin(string? reason = null)
    {
        var viewModel = _services.GetRequiredService<LoginViewModel>();
        viewModel.SignedIn += (_, _) => ShowHome();
        viewModel.ErrorCode = reason;

        ShellContent.Content = new LoginView(viewModel);
    }

    private void ShowHome()
    {
        var viewModel = _services.GetRequiredService<HomeViewModel>();
        viewModel.SignedOut += (_, _) => ShowLogin();

        ShellContent.Content = new HomeView(viewModel, _services);
    }
}
