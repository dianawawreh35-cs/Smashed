namespace CallCenter.Server.Data.Entities;

/// <summary>
/// One place the restaurant delivers to: which branch covers it and what the
/// delivery costs. Table <c>delivery_areas</c> (A-65, S-58).
/// </summary>
/// <remarks>
/// An agent taking an order has to answer two questions before anything else:
/// which branch is this order going to, and what does delivery cost. Both were
/// on paper lists, one per branch. This is those lists.
///
/// <b>One area belongs to one branch.</b> The unique index is on
/// <see cref="NameNormalised"/> alone, not on the pair with the branch, and that
/// is deliberate: the agent's question is "who delivers to Kafr Aqab?", and an
/// area listed under two branches has no answer to it. The restaurant's own
/// lists work this way — 230 areas across four branches, none shared.
/// </remarks>
public class DeliveryArea
{
    public Guid Id { get; set; }

    /// <summary>As the supervisor typed it, and as the agent reads it.</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// <see cref="Name"/> reduced for comparison by
    /// <c>CallCenter.Shared.Text.NameNormalizer</c> — hamza forms folded,
    /// diacritics stripped, case and spacing levelled.
    /// </summary>
    /// <remarks>
    /// The same treatment contact names get, and for the same reason: these are
    /// Arabic place names typed by different people from handwritten lists, so
    /// <c>البيرة</c> and <c>البيره</c> have to be the same place. Without it an agent
    /// searches, finds nothing, and quotes the wrong price.
    /// </remarks>
    public string NameNormalised { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>
    /// What delivery to this area costs.
    /// </summary>
    /// <remarks>
    /// Zero is a real value, not a missing one: the imported lists price three
    /// areas next to the Rafat branch at 0, which is free delivery for the
    /// street outside. Nothing may treat 0 as "not set".
    /// </remarks>
    public decimal Price { get; set; }

    /// <summary>
    /// False hides it from the agent without losing it. Areas the restaurant
    /// stops serving come back, and deleting the row would lose the price it
    /// had.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
