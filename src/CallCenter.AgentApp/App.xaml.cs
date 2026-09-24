using System.IO;
using System.Windows;
using CallCenter.AgentApp.Data;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Sip;
using CallCenter.AgentApp.ViewModels;
using CallCenter.AgentApp.Views;
using CallCenter.Shared.Contracts.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace CallCenter.AgentApp;

/// <summary>
/// Application entry point. Builds the generic host that will own the SIP
/// stack, the offline buffer, the SignalR connection and every view model.
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

        // Whatever the last shift left behind, before any sign-in: a laptop that
        // starts with no network must still reject blocked callers (A-17).
        _host.Services.GetRequiredService<BlockListCache>().LoadFromDisk();

        // Built now, hidden, so a call only has to show it (A-10).
        _host.Services.GetRequiredService<CallPopupWindow>();

        // The offline buffer, and anything the previous version left in the old
        // queue file (A-04).
        await _host.Services.GetRequiredService<CallLogQueue>().InitialiseAsync();

        // Every finished call reaches the server, or the buffer (A-14), and
        // every recording follows its call there (A-31).
        var reporter = _host.Services.GetRequiredService<CallLogReporter>();
        var calls = _host.Services.GetRequiredService<CallService>();

        reporter.Listen(calls);
        reporter.ListenForRecordings(calls);

        // A report or recording that failed is retried without waiting for the
        // next call (A-04, A-31).
        reporter.RetryEvery(TimeSpan.FromMinutes(1));

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        base.OnStartup(e);
    }

    /// <summary>
    /// Registers application services. The SignalR client is added with its
    /// own feature; audio belongs to the call service.
    /// </summary>
    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.Configure<ServerOptions>(context.Configuration.GetSection(ServerOptions.SectionName));
        services.Configure<DialingOptions>(context.Configuration.GetSection(DialingOptions.SectionName));
        services.Configure<RecordingOptions>(context.Configuration.GetSection(RecordingOptions.SectionName));

        // One session object for the process: every view model asks it who is
        // signed in, rather than passing the answer around.
        services.AddSingleton<AgentSession>();
        services.AddSingleton<AgentSettingsStore>();
        services.AddSingleton<Localizer>();
        services.AddSingleton<SipTransportHost>();
        services.AddSingleton<SipRegistrationService>();
        services.AddSingleton<BlockListCache>();
        services.AddSingleton<PhonePreferences>();
        services.AddSingleton<CallService>();
        services.AddSingleton<DialViewModel>();
        services.AddSingleton<CallerViewModel>();
        // A factory, not a scoped context: the queue is used from SIP threads
        // and from the UI, and a DbContext is not safe to share between them.
        services.AddDbContextFactory<AgentBufferDbContext>(options =>
            options.UseSqlite($"Data Source={AgentBufferDbContext.DatabasePath}"));

        services.AddSingleton<CallLogQueue>();
        services.AddSingleton<CallLogReporter>();
        services.AddSingleton<SignInService>();
        services.AddSingleton<SignedOutByServer>();

        // The UI thread, so the call view model can marshal SIP events onto it.
        services.AddSingleton(_ => Current.Dispatcher);

        services.AddHttpClient<ApiClient>(ApiClient.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ServerOptions>>().Value;

            // The trailing slash matters: without it, a BaseUrl with a path
            // would swallow its last segment when a relative path is appended.
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + '/');
            client.Timeout = options.Timeout;
        });

        // One per process, not transient: the pop-up exists from startup and is
        // shown and hidden, rather than built while the phone is ringing (A-10).
        // One definition, shared. Fetched at sign-in.
        services.AddSingleton<ClassificationCatalog>();
        services.AddSingleton<Audio.RingbackTone>();
        services.AddSingleton<Audio.RingTone>();
        services.AddSingleton<Audio.KeyTone>();

        // Several forms being filled in, never shared: the pop-up has one open
        // during a call and the call log has another for a call that was
        // skipped. One instance between them meant an incoming call wiped out
        // what the agent was typing in the log.
        services.AddTransient<ClassificationFormViewModel>();
        services.AddSingleton<CallViewModel>();
        services.AddSingleton<CallPopupWindow>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ContactsViewModel>();
        services.AddTransient<CallLogViewModel>();
        // One per call log, with it: the recording of the call opened there (A-51).
        services.AddTransient<RecordingPlayerViewModel>();
        services.AddTransient<DeliveryViewModel>();
        // Singleton: the point of it is that a picture fetched on one visit to
        // the menu is still there on the next.
        services.AddSingleton<MenuImageCache>();
        services.AddTransient<MenuViewModel>();

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
            // The pop-up refuses to close while the app is running, so it has to
            // be told the app is really going.
            _host.Services.GetRequiredService<CallPopupWindow>().ShutDown();

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
