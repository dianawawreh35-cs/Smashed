namespace CallCenter.Shared.Contracts.Contacts;

/// <summary>
/// One line of a customer's recent history, as the incoming-call pop-up shows
/// it (A-10).
/// </summary>
/// <remarks>
/// Deliberately not <c>CommunicationDto</c>. That record describes a call for
/// the call log, and it carries no classification: A-10 asks for the last five
/// communications <b>with type and notes</b>, which is what makes the panel
/// worth reading. "Three calls last week" tells an agent nothing; "complaint,
/// cold food, unresolved" tells them how to open their mouth.
/// </remarks>
/// <param name="TypeLabelAr">
/// Both labels travel, as they do for the classification form, and the app
/// picks by language (A-80) rather than the server guessing which shift is on.
/// </param>
/// <param name="Notes">
/// What the agent wrote at the time. The single most useful field here and the
/// reason the panel exists.
/// </param>
public record CallerHistoryDto(
    DateTimeOffset At,
    string Direction,
    string Status,
    int? DurationSec,
    string? TypeName,
    string? TypeLabelAr,
    string? TypeLabelEn,
    string? Notes);

/// <summary>
/// Everything the pop-up shows about a customer beyond their name: their totals
/// and their last few calls (A-10).
/// </summary>
/// <remarks>
/// A second request rather than more fields on the contact lookup. The name,
/// address and VIP badge are what the agent needs in the second before they
/// speak, and they come back from the smaller, cheaper query; this one joins
/// classifications and can fill in a moment later without holding that up.
///
/// The counts are of <b>classified</b> calls only. A call nobody classified is
/// not an order that did not happen — it is a call nobody wrote down — and
/// counting it as neither is the only honest answer available.
/// </remarks>
/// <param name="Orders">Calls classified as an order.</param>
/// <param name="Complaints">Calls classified as a complaint.</param>
/// <param name="Cancellations">Calls classified as a cancellation.</param>
public record CallerCardDto(
    int Orders,
    int Complaints,
    int Cancellations,
    IReadOnlyList<CallerHistoryDto> Recent);
