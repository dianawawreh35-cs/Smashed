using System.Diagnostics;
using CallCenter.Shared.Phone;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Opens the caller's cart on the POS in the default browser when an incoming
/// call is answered (A-85), so the agent can start the order while the customer
/// is still saying hello.
/// </summary>
public class PosCart(IOptions<PosCartOptions> options, ILogger<PosCart> logger)
{
    /// <summary>
    /// The page for <paramref name="number"/>, or null when the number has no
    /// local form: withheld, an extension, or foreign. The POS knows none of
    /// those, and an empty cart page is no help to the agent.
    /// </summary>
    public static string? UrlFor(string template, string? number) =>
        PhoneNormalizer.ToNational(number) is { } national
            ? template.Replace("{number}", Uri.EscapeDataString(national), StringComparison.OrdinalIgnoreCase)
            : null;

    /// <summary>
    /// Opens the page. Never throws and never waits: starting the browser can
    /// take a second, and this runs as the call connects.
    /// </summary>
    public void Open(string? number)
    {
        var settings = options.Value;

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.UrlTemplate))
        {
            return;
        }

        if (UrlFor(settings.UrlTemplate, number) is not { } url)
        {
            logger.LogInformation("No POS cart opened: the caller's number has no local form");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                // No browser, or a policy that stops one being started. The
                // call carries on; the agent can open the POS by hand.
                logger.LogWarning(ex, "Could not open the POS cart in the browser");
            }
        });
    }
}
