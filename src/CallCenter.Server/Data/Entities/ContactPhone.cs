using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// One phone number belonging to a contact. Table <c>contact_phones</c>.
/// </summary>
/// <remarks>
/// Caller matching (A-13): normalise the incoming number, try an exact match on
/// <see cref="Normalised"/>, then fall back to <see cref="Last9"/>, which handles
/// 05x vs 9705x vs +9705x. Use <c>CallCenter.Shared.Phone.PhoneNormalizer</c> to
/// produce both — never hand-roll the rules.
/// </remarks>
public class ContactPhone
{
    public Guid Id { get; set; }

    public Guid ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    /// <summary>As entered by the agent, or as received from the PBX.</summary>
    public string Raw { get; set; } = null!;

    /// <summary>Digits only, E.164 without '+', e.g. <c>970599123456</c>. Unique.</summary>
    public string Normalised { get; set; } = null!;

    /// <summary>
    /// Database-generated: <c>right(normalised, 9)</c>. Read-only from the
    /// application — assigning to it has no effect.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public string Last9 { get; private set; } = null!;

    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
