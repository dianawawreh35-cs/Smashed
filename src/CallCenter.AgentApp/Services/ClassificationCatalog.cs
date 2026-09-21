using CallCenter.Shared.Contracts.Classifications;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// The classification form as the supervisor defined it, fetched once and
/// shared (A-40, S-40).
/// </summary>
/// <remarks>
/// Split out from the form the agent fills in, because there is <b>one</b>
/// definition and <b>several</b> forms being filled in: the call pop-up has one
/// open during a call, and the call log has another for tidying up a call that
/// was skipped. Sharing a single form view model between them meant an incoming
/// call wiped out whatever the agent was typing in the log.
///
/// Fetched at sign-in so the fields are in hand before the first call, rather
/// than being fetched while an agent waits with a customer on the line. That is
/// also how a supervisor's change reaches agents without anything being
/// reinstalled.
/// </remarks>
public class ClassificationCatalog(ApiClient api, ILogger<ClassificationCatalog> logger)
{
    /// <summary>The current form, or null when it could not be fetched.</summary>
    public ClassificationFormDto? Form { get; private set; }

    /// <summary>Whether a form can be drawn at all.</summary>
    public bool IsReady => Form is not null;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var result = await api.GetClassificationFormAsync(ct);

        if (result.IsOk && result.Value is { } form)
        {
            Form = form;
            logger.LogInformation("Classification form version {Version} loaded", form.Version);
            return;
        }

        // Not fatal. Calls still work; they simply cannot be classified until
        // the next sign-in, and the screens say so rather than showing an empty
        // form that cannot be saved.
        logger.LogWarning(
            "The classification form could not be loaded ({Code}); classifying is unavailable",
            result.ErrorCode);
    }
}
