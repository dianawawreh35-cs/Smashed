namespace CallCenter.Shared.Contracts.Pos;

/// <summary>
/// The POS customer lookup (A-67) as the settings screen shows it: whether it
/// is on, and what its last run did.
/// </summary>
/// <param name="Enabled">The server has a POS token. Without one nothing is ever asked.</param>
/// <param name="IntervalMinutes">How often it runs by itself (<c>pos.lookup.interval_minutes</c>).</param>
/// <param name="Running">A run is going now.</param>
/// <param name="LastStartedAt">When the last run since the server started began; null when there has been none.</param>
/// <param name="LastFinishedAt">When it ended.</param>
/// <param name="Asked">Numbers the last run asked the POS about.</param>
/// <param name="NotFound">Of those, the ones the POS did not know.</param>
/// <param name="Created">Contacts it created.</param>
/// <param name="FilledIn">Existing contacts it filled in.</param>
/// <param name="CallsLinked">Calls it attached to a contact.</param>
/// <param name="Failed">The POS could not be asked, so the run stopped early.</param>
public record PosLookupStatusDto(
    bool Enabled,
    int IntervalMinutes,
    bool Running,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastFinishedAt,
    int Asked,
    int NotFound,
    int Created,
    int FilledIn,
    int CallsLinked,
    bool Failed);
