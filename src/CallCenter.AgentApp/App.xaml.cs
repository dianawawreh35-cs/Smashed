using System.IO;
using System.Windows;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.ViewModels;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace CallCenter.AgentApp;

/// <summary>
/// Application entry point. Builds the generic host that will own the SIP
/// stack, the SignalR connection, the Sqlite offline buffer and every view
/// model; for now it only wires up configuration, logging and the shell window.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>Per-user application data root: <c>%LOCALAPPDATA%\CallCenter</c>.</summary>
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CallCenter");

    /// <summary>Log directory: <c>%LOCALAPPDATA%\CallCenter\logs</c>.</summary>
    public static string LogDirectory { get; } = Path.Combine(AppDataDirectory, "logs");

    /// <summary>Service provider for views that cannot take constructor injection.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("The host has not been started yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(LogDirectory, "agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
        DispatcherUnhandledException += (_, args) =>
            Log.Error(args.Exception, "Unhandled dispatcher exception");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration(config => config
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("CALLCENTER_"))
            .ConfigureServices(ConfigureServices)
            .Build();

        await _host.StartAsync();

        Log.Information("Agent App started. Logs: {LogDirectory}", LogDirectory);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        base.OnStartup(e);
    }

    /// <summary>
    /// Registers application services. SIP, audio, the SignalR client and the
    /// Sqlite offline buffer are added with their respective features.
    /// </summary>
    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.Configure<ServerOptions>(context.Configuration.GetSection(ServerOptions.SectionName));

        // One session object for the process: every view model asks it who is
        // signed in, rather than passing the answer around.
        services.AddSingleton<AgentSession>();
        services.AddSingleton<AgentSettingsStore>();
        services.AddSingleton<Localizer>();
        services.AddSingleton<SignInService>();

        services.AddHttpClient<ApiClient>(ApiClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ServerOptions>>().Value;

            // The trailing slash matters: without it, a BaseUrl with a path
            // would swallow its last segment when a relative path is appended.
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + '/');
            client.Timeout = options.Timeout;
        });

        services.AddTransient<LoginViewModel>();
        services.AddTransient<HomeViewModel>();

        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// Best-effort sign-out during shutdown. Failures are swallowed: the app is
    /// closing either way, and the idle timer closes a session left open.
    /// </summary>
    private async Task SignOutOnExitAsync()
    {
        try
        {
            var session = _host!.Services.GetRequiredService<AgentSession>();
            if (!session.IsSignedIn)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            await _host.Services.GetRequiredService<SignInService>()
                .SignOutAsync(LogoutReasons.AppClosed, timeout.Token);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not close the session during shutdown");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Close the server-side session so a laptop that is simply shut down
            // does not look signed in until the idle timer catches it (A-05).
            await SignOutOnExitAsync();

            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.Information("Agent App stopped");
        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }
}
