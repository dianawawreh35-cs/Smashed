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

        // Never bigger than the screen it opens on (review, 27 Sep). A
        // 1366 x 768 laptop at 125 % scaling has about 1093 x 574 to offer
        // above the taskbar: the old minimum width, 1120, did not fit at all,
        // and the opening height ran off the bottom. The minimum is now 1024.
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height));

        // The server can end the sign-in from its side (N-05). Back to the
        // sign-in screen, saying why, rather than a main screen that fails at
        // every turn.
        services.GetRequiredService<SignedOutByServer>().SignedOut += (_, _) =>
            Dispatcher.Invoke(() => ShowLogin(LoginErrorCodes.SignedOut));

        // F-02: what went wrong and was survived, across the top.
        services.GetRequiredService<AgentNotices>().Posted += (_, key) =>
            Dispatcher.BeginInvoke(() => ShowNotice(key));
        localizer.LanguageChanged += (_, _) => ShowNotice(_noticeKey);

        // M-A05: the call's keys work from here as well as from the pop-up.
        CallShortcuts.Attach(this, services.GetRequiredService<CallViewModel>());

        ShowLogin();
    }

    /// <summary>
    /// The scope the screen on show was built from (M-A06). Each sign-in and
    /// each return to the sign-in screen gets a new one, and the old one is
    /// disposed with everything it made, so the last shift's screens stop
    /// listening to the language, the phone and the queue.
    /// </summary>
    private IServiceScope? _shell;

    /// <summary>The view on show, disposed with its scope.</summary>
    private IDisposable? _shellView;

    /// <summary>Ends the screen on show, and starts a scope for the next.</summary>
    private IServiceProvider NewShell()
    {
        _shellView?.Dispose();
        _shellView = null;

        _shell?.Dispose();
        _shell = _services.CreateScope();

        return _shell.ServiceProvider;
    }

    /// <summary>The notice on screen, as a label key, so it follows the language.</summary>
    private string? _noticeKey;

    private void ShowNotice(string? key)
    {
        _noticeKey = key;
        NoticeText.Text = key is null ? string.Empty : Localizer[key];
        NoticeBar.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDismissNotice(object sender, RoutedEventArgs e) => ShowNotice(null);

    /// <summary>Bound by the window's XAML for its title and direction.</summary>
    public Localizer Localizer { get; }

    /// <param name="reason">
    /// One of <see cref="LoginErrorCodes"/>, shown under the password box, when
    /// the agent did not sign out themselves.
    /// </param>
    private void ShowLogin(string? reason = null)
    {
        var services = NewShell();

        var viewModel = services.GetRequiredService<LoginViewModel>();
        viewModel.SignedIn += (_, _) => ShowHome();
        viewModel.ErrorCode = reason;

        ShellContent.Content = new LoginView(viewModel);
    }

    private void ShowHome()
    {
        var services = NewShell();

        var viewModel = services.GetRequiredService<HomeViewModel>();
        viewModel.SignedOut += (_, _) => ShowLogin();

        var view = new HomeView(viewModel, services);
        _shellView = view;
        ShellContent.Content = view;
    }

    protected override void OnClosed(EventArgs e)
    {
        _shellView?.Dispose();
        _shell?.Dispose();
        base.OnClosed(e);
    }
}
