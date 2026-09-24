using CallCenter.Shared;
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
    /// <summary>The form for calls that came in, or null when it could not be fetched.</summary>
    public ClassificationFormDto? Inbound { get; private set; }

    /// <summary>The form for calls this agent placed, or null when it could not be fetched.</summary>
    public ClassificationFormDto? Outbound { get; private set; }

    /// <summary>
    /// The form for a call in the given direction. Kept as two forms rather
    /// than one with rules, because an outbound call is a different
    /// conversation and the supervisor designs it separately (S-40).
    /// </summary>
    public ClassificationFormDto? FormFor(bool outbound) => outbound ? Outbound : Inbound;

    /// <summary>Whether a form can be drawn at all.</summary>
    public bool IsReady => Inbound is not null || Outbound is not null;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Inbound = await FetchAsync(Directions.In, ct);
        Outbound = await FetchAsync(Directions.Out, ct);
    }

    private async Task<ClassificationFormDto?> FetchAsync(string direction, CancellationToken ct)
    {
        var result = await api.GetClassificationFormAsync(direction, ct);

        if (result.IsOk && result.Value is { } form)
        {
            logger.LogInformation(
                "Classification form version {Version} ({Direction}) loaded", form.Version, direction);
            return form;
        }

        // Not fatal. Calls still work; they simply cannot be classified until
        // the next sign-in, and the screens say so rather than showing an empty
        // form that cannot be saved.
        logger.LogWarning(
            "The {Direction} classification form could not be loaded ({Code}); classifying is unavailable",
            direction, result.ErrorCode);
        return null;
    }
}
