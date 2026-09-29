using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.Shared.Contracts.AgentApp;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// The bar that says a newer Agent App is ready, and installs it when the
/// agent presses Update now (A-82).
/// </summary>
/// <remarks>
/// <b>The agent chooses when (Dia, 29 Sep).</b> Updating closes the app for
/// about half a minute, and the phone with it. So nothing installs on its
/// own: the bar offers it, and the button is off while a call is on, or while
/// the pop-up still holds a form or notes being typed, since the new copy
/// would start without them.
///
/// <b>Asked at sign-in and every 15 minutes</b>, so a version uploaded during
/// the day reaches a laptop that stays signed in all shift.
///
/// <b>Only the installed copy updates.</b> The installer always writes to
/// <see cref="InstallFolder"/>. A copy run from anywhere else, a developer's
/// build or a zip unpacked into a second folder, would install a different
/// app there and leave itself as it was, and two copies are what took each
/// other's calls on 27 Sep (N-05).
///
/// <b>No prompt on the way.</b> The installer is fetched by this app, not a
/// browser, so it carries no "from the internet" mark and Windows shows no
/// "protected your PC" screen; it needs no administrator rights; and it runs
/// with <c>/VERYSILENT</c>, then starts the new version itself.
/// </remarks>
public partial class AppUpdateViewModel : ObservableObject
{
    /// <summary>Where the installer puts the app, always (installer.iss, <c>DefaultDirName</c>).</summary>
    public const string InstallFolder = @"C:\SmashedAgentApp";

    /// <summary>Where the downloaded installer waits to be run, beside the app's other data.</summary>
    private static readonly string UpdatesFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CallCenter", "updates");

    private readonly ApiClient _api;
    private readonly AgentSession _session;
    private readonly CallService _calls;
    private readonly CallViewModel _call;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<AppUpdateViewModel> _logger;

    /// <summary>One check at a time: sign-in and the timer can land together.</summary>
    private readonly SemaphoreSlim _checking = new(1, 1);

    /// <summary>What the server offers, when it is newer than this copy.</summary>
    private AgentAppInstallerDto? _offered;

    public AppUpdateViewModel(
        ApiClient api,
        AgentSession session,
        CallService calls,
        CallViewModel call,
        Localizer localizer,
        Dispatcher dispatcher,
        ILogger<AppUpdateViewModel> logger)
    {
        _api = api;
        _session = session;
        _calls = calls;
        _call = call;
        _dispatcher = dispatcher;
        _logger = logger;
        Localizer = localizer;

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Headline));
            OnPropertyChanged(nameof(Status));
        };

        // Whatever can make updating now lose something: the call, and what
        // the pop-up is still holding after it.
        calls.StateChanged += (_, _) => _dispatcher.BeginInvoke(RefreshBusy);
        call.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CallViewModel.IsNotesOpen))
            {
                _dispatcher.BeginInvoke(RefreshBusy);
            }
        };
        call.Classification.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClassificationFormViewModel.IsUnfinished))
            {
                _dispatcher.BeginInvoke(RefreshBusy);
            }
        };
    }

    public Localizer Localizer { get; }

    /// <summary>Whether this copy is the installed one, the only one that may update itself.</summary>
    public static bool IsInstalledCopy =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            InstallFolder,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The newer version on offer, or null when this copy is current.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffered), nameof(Headline))]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    private string? _availableVersion;

    /// <summary>Downloading, or handing over to the installer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    private bool _isUpdating;

    /// <summary>Why the last attempt did not install, as a label key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private string? _failureKey;

    /// <summary>The bar is on screen.</summary>
    public bool IsOffered => AvailableVersion is not null;

    public string Headline => Localizer["update.ready"].Replace("{version}", AvailableVersion ?? string.Empty);

    /// <summary>The line under the headline: what pressing the button will do, or why it cannot yet.</summary>
    public string Status =>
        IsUpdating ? Localizer["update.downloading"]
        : IsBusyWithCall ? Localizer["update.afterCall"]
        : FailureKey is { } key ? Localizer[key]
        : Localizer["update.howLong"];

    /// <summary>A call is on, or the pop-up still holds something the agent is typing.</summary>
    private bool IsBusyWithCall =>
        _calls.State.IsActive || _call.Classification.IsUnfinished || _call.IsNotesOpen;

    private void RefreshBusy()
    {
        OnPropertyChanged(nameof(Status));
        UpdateNowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Asks now, at every sign-in, and every <paramref name="interval"/>
    /// after. Called once, at start-up.
    /// </summary>
    public void CheckEvery(TimeSpan interval)
    {
        if (!IsInstalledCopy)
        {
            _logger.LogInformation(
                "Running from {Folder}, not {InstallFolder}: updates are not offered to this copy",
                AppContext.BaseDirectory, InstallFolder);
            return;
        }

        _session.Changed += (_, _) =>
        {
            if (_session.IsSignedIn)
            {
                _ = CheckAsync();
            }
        };

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync())
            {
                await CheckAsync();
            }
        });
    }

    /// <summary>
    /// One check. The server answers only a signed-in app, so signed out it
    /// does nothing, and a bar already up stays up: the sign-in screen, with
    /// no call possible, is the best moment to update.
    /// </summary>
    private async Task CheckAsync()
    {
        if (_session.AccessToken is null || IsUpdating || !await _checking.WaitAsync(0))
        {
            return;
        }

        try
        {
            var result = await _api.GetAgentAppInstallerAsync();

            if (!result.IsOk || result.Value is not { } current)
            {
                // No upload yet, or the server is away: ask again next time.
                return;
            }

            var offered = IsNewer(current.Version, LaptopInfo.AppVersion) ? current : null;

            await _dispatcher.InvokeAsync(() =>
            {
                if (offered is not null && offered.Version != AvailableVersion)
                {
                    _logger.LogInformation(
                        "Version {Offered} is ready; this copy is {Own}", offered.Version, LaptopInfo.AppVersion);
                }

                _offered = offered;
                AvailableVersion = offered?.Version;
            });
        }
        catch (Exception ex)
        {
            // Never out of the timer's loop: that would end checking for the
            // rest of the shift.
            _logger.LogWarning(ex, "Checking for a newer Agent App failed");
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>A version that cannot be read is never offered: better no update than a wrong one.</summary>
    public static bool IsNewer(string offered, string own) =>
        Version.TryParse(offered, out var theirs)
        && Version.TryParse(own, out var ours)
        && theirs > ours;

    private bool CanUpdate => IsOffered && !IsUpdating && !IsBusyWithCall;

    /// <summary>
    /// Downloads the installer, starts it silently and closes the app, which
    /// signs out and takes the phone off the PBX on the way (M-A01). The
    /// installer waits for the app to go, replaces it, and starts the new one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateNowAsync()
    {
        if (_offered is not { } offered || !Version.TryParse(offered.Version, out var version))
        {
            return;
        }

        IsUpdating = true;
        FailureKey = null;

        try
        {
            Directory.CreateDirectory(UpdatesFolder);
            ClearUpdatesFolder();

            // The parsed version, never the text as uploaded, goes into a path.
            var installer = Path.Combine(UpdatesFolder, $"SmashedAgentApp-Setup-{version}.exe");
            var download = await _api.DownloadAgentAppInstallerAsync(installer, offered.SizeBytes);

            if (!download.IsOk)
            {
                FailureKey = "update.failed";
                return;
            }

            // A call can ring in the seconds the download took. It wins: the
            // bar says so, and the button comes back when the call is over.
            if (IsBusyWithCall)
            {
                _logger.LogInformation("A call came in during the download; the update waits for it");
                return;
            }

            // Started before the app closes, so a failure to start leaves the
            // agent with a working app and a message rather than no app at all.
            Process.Start(new ProcessStartInfo(installer)
            {
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{Path.Combine(UpdatesFolder, $"install-{version}.log")}\"",
                UseShellExecute = false,
                WorkingDirectory = UpdatesFolder,
            });

            _logger.LogInformation(
                "Updating from {Own} to {Offered}: the installer is running, and the app closes for it",
                LaptopInfo.AppVersion, version);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The update could not be started");
            FailureKey = "update.failed";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    /// <summary>
    /// The last update's installer and log, and anything half-downloaded. An
    /// installer kept after use is 70 MB of disk for nothing.
    /// </summary>
    private void ClearUpdatesFolder()
    {
        foreach (var file in Directory.EnumerateFiles(UpdatesFolder))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("Could not remove {File} ({Reason})", file, ex.Message);
            }
        }
    }
}
