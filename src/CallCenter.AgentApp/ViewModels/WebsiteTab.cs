using System.Text.Json;
using System.Windows;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.Services.Websites;
using CallCenter.Shared.Contracts.Websites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// One website tab and the browser inside it (A-88).
/// </summary>
/// <remarks>
/// <b>Its own logins.</b> The browser is made with a profile for this agent
/// and this tab (<see cref="WebsiteEngine.ProfileName"/>), so its cookies,
/// storage and saved passwords are its own: four tabs on one site are four
/// accounts signed in at once, and the next agent on the laptop has none of
/// them.
///
/// <b>The browser is made once and never moved.</b> The Websites screen puts
/// it in one of its places by changing its row and column, not its parent: a
/// WebView2 is a window of its own inside ours, and taking it out of the tree
/// can close it, page and login state with it.
///
/// <b>Started when needed.</b> A tab that alerts with sound starts at sign-in,
/// so its dings are heard before anyone opens it; the others the first time
/// they are shown, or the cart is opened in them.
///
/// <b>A copy</b> (Dia, 4 Oct 2026: two POS tabs at once) is the agent's own
/// second, third or fourth tab on a site, named "POS 2" and so on. It shares
/// the original's profile, so it is signed in as the original is, and its
/// popups too. It never takes the caller's cart, and is not started at
/// sign-in for its sound: the original is.
/// </remarks>
public sealed partial class WebsiteTab : ObservableObject, IDisposable
{
    private static readonly int[] RetryDelaysMs = [300, 1200, 3000];

    private readonly Guid _agentId;
    private readonly WebsiteEngine _engine;
    private readonly ILogger _logger;
    private readonly AutoLogin.Gate _gate = new();

    private Task? _start;
    private string? _pendingUrl;
    private CancellationTokenSource? _loginTries;
    private bool _callSilence;
    private bool _disposed;

    /// <param name="number">1 for the supervisor's tab itself, 2 to 4 for the agent's copies of it.</param>
    /// <param name="startUrl">Where a new copy opens: the page the original was on. Null for the start page.</param>
    public WebsiteTab(
        AgentWebsiteDto site, Guid agentId, WebsiteEngine engine, Localizer localizer, ILogger logger,
        int number = 1, string? startUrl = null)
    {
        Site = site;
        Number = number;
        Id = WebsiteEngine.TabId(site.Id, number);
        _agentId = agentId;
        _engine = engine;
        _logger = logger;
        _pendingUrl = startUrl;
        Localizer = localizer;

        Browser = new WebView2
        {
            Visibility = Visibility.Collapsed,
            DefaultBackgroundColor = System.Drawing.Color.White,
        };
    }

    public AgentWebsiteDto Site { get; }

    /// <summary>The site's own id for the original; an id of its own for a copy, so the layout and groups can name it.</summary>
    public Guid Id { get; }

    /// <summary>1 for the original, 2 to 4 for a copy.</summary>
    public int Number { get; }

    /// <summary>The agent made this tab as a copy of the supervisor's, and can close it.</summary>
    public bool IsCopy => Number > 1;

    public Localizer Localizer { get; }

    /// <summary>The browser. Placed by the Websites screen, never re-parented.</summary>
    public WebView2 Browser { get; }

    /// <summary>The tab's name in the agent's language, with its number for a copy.</summary>
    public string Name => (Localizer.IsArabic ? Site.NameAr : Site.NameEn) + (IsCopy ? $" {Number}" : string.Empty);

    /// <summary>The page shown now, for a copy to open on. Null before the tab has started.</summary>
    public string? CurrentUrl => Browser.CoreWebView2?.Source ?? _pendingUrl;

    /// <summary>Started at sign-in and never asked to save memory, so its sound is heard while hidden. The original only.</summary>
    public bool AlertsWithSound => Site.AlertsWithSound && !IsCopy;

    /// <summary>The site makes its logins itself, with the supervisor's username and password.</summary>
    public bool IsShared => Site.Login == WebsiteLogins.Shared;

    /// <summary>
    /// The app types a login in: the supervisor's shared one, or, for a site
    /// on the agent's own login (the POS), the agent's Call Center username
    /// and password (A-88).
    /// </summary>
    private bool SignsIn => !string.IsNullOrEmpty(Site.Password);

    /// <summary>The caller's cart opens here (A-85): the original only, so it always lands in the same tab.</summary>
    public bool OpensCart => !IsCopy && !string.IsNullOrWhiteSpace(Site.CartUrl);

    /// <summary>The page is playing sound: the speaker mark on the tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundGlyph))]
    private bool _isPlayingAudio;

    /// <summary>The agent muted this tab. Separate from the silence of a call, which comes and goes by itself.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundGlyph))]
    [NotifyPropertyChangedFor(nameof(MuteLabel))]
    private bool _isMutedByAgent;

    /// <summary>1.0 is 100 %.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private double _zoom = 1.0;

    /// <summary>Something the agent should know about this tab, as a label key. Null for nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusKey;

    [ObservableProperty]
    private bool _canGoBack;

    public string? Status => StatusKey is null ? null : Localizer[StatusKey];

    public bool HasStatus => StatusKey is not null;

    public string ZoomText => $"{Math.Round(Zoom * 100)}%";

    /// <summary>Segoe Fluent Icons: muted, sound, or a quiet speaker.</summary>
    public string SoundGlyph => IsMutedByAgent ? "" : IsPlayingAudio ? "" : "";

    public string MuteLabel => Localizer[IsMutedByAgent ? "websites.unmute" : "websites.mute"];

    /// <summary>Makes the browser and opens the site, once. Safe to call again.</summary>
    public Task StartAsync() => _start ??= StartCoreAsync();

    /// <summary>Opens <paramref name="url"/> in this tab, starting it first if need be.</summary>
    public void Navigate(string url)
    {
        if (Browser.CoreWebView2 is { } core)
        {
            core.Navigate(url);
        }
        else
        {
            _pendingUrl = url;
            _ = StartAsync();
        }
    }

    /// <summary>
    /// Silent for the length of a call (A-88): the agent is listening to the
    /// customer. The agent's own mute stays as it was.
    /// </summary>
    public void SetCallSilence(bool silent)
    {
        _callSilence = silent;
        ApplyMute();
    }

    /// <summary>
    /// The tab is on screen or not. A tab that does not alert is asked to use
    /// less memory while hidden; one that alerts is left alone.
    /// </summary>
    public void SetShown(bool shown)
    {
        Browser.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        if (Browser.CoreWebView2 is { } core && !AlertsWithSound)
        {
            core.MemoryUsageTargetLevel = shown
                ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Browser.CoreWebView2 is { CanGoBack: true } core)
        {
            core.GoBack();
        }
    }

    /// <summary>Loads the page again, and lets a refused login try once more.</summary>
    [RelayCommand]
    private void Reload()
    {
        _gate.Reset();
        StatusKey = null;
        Browser.CoreWebView2?.Reload();
    }

    [RelayCommand]
    private void Home()
    {
        _gate.Reset();
        StatusKey = null;
        Navigate(Site.Url);
    }

    /// <summary>
    /// The page as a PDF in Downloads (Dia, 4 Oct 2026). Says so on the bar
    /// for a few seconds, then gets out of the way.
    /// </summary>
    [RelayCommand]
    private async Task SavePdfAsync()
    {
        if (Browser.CoreWebView2 is not { } core)
        {
            return;
        }

        try
        {
            var path = await PagePdf.SaveAsync(core, Name);
            StatusKey = path is null ? "websites.pdfFailed" : "websites.pdfSaved";

            if (path is not null)
            {
                _logger.LogInformation("Website tab {Name}: page saved as PDF to {Path}", Site.NameEn, path);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                       or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Website tab {Name}: the page could not be saved as PDF", Site.NameEn);
            StatusKey = "websites.pdfFailed";
        }

        // Not awaited: the button is free again as soon as the file is written.
        _ = ClearStatusLaterAsync(StatusKey);
    }

    /// <summary>A message on the bar for a few seconds, as a label key.</summary>
    public void Say(string key)
    {
        StatusKey = key;
        _ = ClearStatusLaterAsync(key);
    }

    /// <summary>Takes a message off the bar after a while, unless another has replaced it.</summary>
    private async Task ClearStatusLaterAsync(string? shown)
    {
        await Task.Delay(TimeSpan.FromSeconds(6));

        if (StatusKey == shown)
        {
            StatusKey = null;
        }
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(3.0, Math.Round(Zoom + 0.1, 1));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.3, Math.Round(Zoom - 0.1, 1));

    [RelayCommand]
    private void ToggleMute() => IsMutedByAgent = !IsMutedByAgent;

    partial void OnZoomChanged(double value) => Browser.ZoomFactor = value;

    partial void OnIsMutedByAgentChanged(bool value) => ApplyMute();

    /// <summary>The names follow the language (A-80).</summary>
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(MuteLabel));
    }

    private void ApplyMute()
    {
        if (Browser.CoreWebView2 is { } core)
        {
            core.IsMuted = _callSilence || IsMutedByAgent;
        }
    }

    private async Task StartCoreAsync()
    {
        try
        {
            if (await _engine.GetAsync() is not { } environment || _disposed)
            {
                StatusKey = "websites.noEngine";
                return;
            }

            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = WebsiteEngine.ProfileName(_agentId, Site.Id);

            await Browser.EnsureCoreWebView2Async(environment, options);

            if (_disposed)
            {
                return;
            }

            var core = Browser.CoreWebView2;

            // No developer tools: they would show the password the app typed.
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            // The agent's own login is remembered for them, as Edge would, for
            // when the site refuses their Call Center one and they type it.
            // A shared one is typed by the app every time and not saved, so
            // it is in the profile nowhere but the site's cookie.
            core.Settings.IsPasswordAutosaveEnabled = !IsShared;
            core.Settings.IsGeneralAutofillEnabled = true;

            core.IsDocumentPlayingAudioChanged += (_, _) => IsPlayingAudio = core.IsDocumentPlayingAudio;
            core.HistoryChanged += (_, _) => CanGoBack = core.CanGoBack;
            core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess)
                {
                    TryLoginSoon();
                }
            };
            core.SourceChanged += (_, e) =>
            {
                // A page that changes its address without loading a new
                // document: a login screen can arrive that way too.
                if (!e.IsNewDocument)
                {
                    TryLoginSoon();
                }
            };
            core.NewWindowRequested += OnNewWindowRequested;

            Browser.ZoomFactor = Zoom;
            ApplyMute();
            SetShown(Browser.Visibility == Visibility.Visible);

            if (IsShared && string.IsNullOrEmpty(Site.Password))
            {
                StatusKey = "websites.noPassword";
            }

            core.Navigate(_pendingUrl ?? Site.Url);
            _pendingUrl = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                       or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "Website tab {Name} could not start", Site.NameEn);
            StatusKey = "websites.couldNotStart";
        }
    }

    /// <summary>
    /// The login is typed in when the site shows its login page. Tried a few
    /// times over three seconds, because a page built by script draws its
    /// boxes after it has loaded.
    /// </summary>
    private void TryLoginSoon()
    {
        if (!SignsIn || _disposed)
        {
            return;
        }

        _loginTries?.Cancel();
        _loginTries?.Dispose();
        var tries = new CancellationTokenSource();
        _loginTries = tries;

        _ = TryLoginAsync(tries.Token);
    }

    private async Task TryLoginAsync(CancellationToken ct)
    {
        try
        {
            foreach (var delay in RetryDelaysMs)
            {
                await Task.Delay(delay, ct);

                if (Browser.CoreWebView2 is not { } core
                    || !AutoLogin.MayFillOn(Site.Url, Uri.TryCreate(core.Source, UriKind.Absolute, out var page) ? page : null))
                {
                    return;
                }

                // Is there a login form at all? Asked without the password
                // first, so a page with none never has it put in it.
                if (await core.ExecuteScriptAsync("!!document.querySelector('input[type=password]')") != "true"
                    && string.IsNullOrEmpty(Site.PasswordSelector))
                {
                    continue;
                }

                if (!_gate.MayTry())
                {
                    StatusKey = IsShared ? "websites.loginFailed" : "websites.ownLoginFailed";
                    _logger.LogWarning("Website tab {Name}: the login page came back after signing in; not trying again", Site.NameEn);
                    return;
                }

                var answer = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(AutoLogin.Script(Site)));

                if (answer == AutoLogin.Submitted)
                {
                    _gate.Submitted();
                    StatusKey = null;
                    _logger.LogInformation("Website tab {Name}: signed in with the {Login} login", Site.NameEn, Site.Login);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A newer page replaced this one.
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException
                                       or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Website tab {Name}: the automatic login failed", Site.NameEn);
        }
    }

    /// <summary>
    /// A link or a script that opens a new window: a receipt to print, say.
    /// It opens in a window of its own, signed in as this tab is, and silent
    /// when the tab is.
    /// </summary>
    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();

        try
        {
            if (await _engine.GetAsync() is not { } environment)
            {
                return;
            }

            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = WebsiteEngine.ProfileName(_agentId, Site.Id);

            var popup = new Views.WebsitePopupWindow(Name)
            {
                Owner = Application.Current.MainWindow,
                FlowDirection = Localizer.FlowDirection,
            };
            popup.Show();

            await popup.Browser.EnsureCoreWebView2Async(environment, options);
            popup.Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            popup.Browser.CoreWebView2.IsMuted = _callSilence || IsMutedByAgent;

            e.NewWindow = popup.Browser.CoreWebView2;
            e.Handled = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Website tab {Name}: a new window could not be opened", Site.NameEn);
        }
        finally
        {
            deferral.Complete();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _loginTries?.Cancel();
        _loginTries?.Dispose();
        Browser.Dispose();
    }
}
