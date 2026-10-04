using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace CallCenter.AgentApp.Services.Websites;

/// <summary>
/// The Edge engine the website tabs run on (A-88): WebView2, which Windows 10
/// and 11 already carry, so the app ships a few MB of DLLs and no browser.
/// </summary>
/// <remarks>
/// <b>One environment for the process.</b> Every tab shares it; what keeps the
/// tabs' logins apart is the <b>profile</b> each is created with
/// (<see cref="ProfileName"/>), not a folder each. Its data lives under
/// <c>%LOCALAPPDATA%\CallCenter\Browser</c>, with the app's other data, so an
/// upgrade or a reinstall of the app keeps the agents' logins.
///
/// <b>Tabs that alert are never put to sleep.</b> Chromium slows the timers of
/// a page nobody is looking at, and after five minutes runs them once a
/// minute; a site that checks for new orders on a timer would then ding a
/// minute late. The switches below turn that off for every tab. It costs a
/// little more processor time on hidden pages, and is the point.
///
/// <b>A laptop without the runtime</b> gets null from <see cref="GetAsync"/>:
/// the Websites screen says so, and the POS cart opens in the default browser
/// as before (A-85).
/// </remarks>
public sealed class WebsiteEngine(ILogger<WebsiteEngine> logger)
{
    /// <summary>Where the profiles live: <c>%LOCALAPPDATA%\CallCenter\Browser</c>.</summary>
    public static string DataDirectory { get; } = Path.Combine(App.AppDataDirectory, "Browser");

    private const string BrowserArguments =
        "--disable-background-timer-throttling "
        + "--disable-renderer-backgrounding "
        + "--disable-backgrounding-occluded-windows "
        + "--disable-features=IntensiveWakeUpThrottling";

    private Task<CoreWebView2Environment?>? _environment;

    /// <summary>The shared environment, made on first use. Null when the runtime is missing or broken.</summary>
    public Task<CoreWebView2Environment?> GetAsync() => _environment ??= CreateAsync();

    /// <summary>
    /// The profile for one agent's copy of one tab. Per agent, because the
    /// laptops are shared between shifts: the next agent to sign in must not
    /// open the last one's POS account. At most 64 characters, as WebView2
    /// allows.
    /// </summary>
    public static string ProfileName(Guid agentId, Guid websiteId) =>
        $"a{agentId:N}-{websiteId.ToString("N")[..12]}";

    /// <summary>
    /// A tab's id: the website's own for the original (<paramref name="number"/>
    /// 1), and for the agent's copy number 2, 3 or 4 an id worked out from the
    /// website's, the same every time, so the layout and the groups saved by id
    /// find the copy again at the next sign-in.
    /// </summary>
    public static Guid TabId(Guid websiteId, int number)
    {
        if (number <= 1)
        {
            return websiteId;
        }

        var bytes = websiteId.ToByteArray();
        bytes[15] ^= (byte)number;
        bytes[14] ^= 0xC0;
        return new Guid(bytes);
    }

    private async Task<CoreWebView2Environment?> CreateAsync()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Directory.CreateDirectory(DataDirectory);

            var options = new CoreWebView2EnvironmentOptions(BrowserArguments)
            {
                // A-80: the sites' own language prompts follow Windows, not us;
                // nothing to set. Passwords: saved per profile (A-88).
            };

            var environment = await CoreWebView2Environment.CreateAsync(null, DataDirectory, options);
            logger.LogInformation("Website engine ready: WebView2 runtime {Version}", version);
            return environment;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or IOException
                                       or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            logger.LogWarning(ex, "The WebView2 runtime is missing or would not start; the websites are off on this laptop");
            return null;
        }
    }
}
