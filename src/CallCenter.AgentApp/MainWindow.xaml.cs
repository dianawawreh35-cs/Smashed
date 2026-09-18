using System.Windows;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.ViewModels;
using CallCenter.AgentApp.Views;
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

        ShowLogin();
    }

    /// <summary>Bound by the window's XAML for its title and direction.</summary>
    public Localizer Localizer { get; }

    private void ShowLogin()
    {
        var viewModel = _services.GetRequiredService<LoginViewModel>();
        viewModel.SignedIn += (_, _) => ShowHome();

        ShellContent.Content = new LoginView(viewModel);
    }

    private void ShowHome()
    {
        var viewModel = _services.GetRequiredService<HomeViewModel>();
        viewModel.SignedOut += (_, _) => ShowLogin();

        ShellContent.Content = new HomeView(viewModel, _services);
    }
}
