namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A mistake made by a branch or by an agent, recorded by a supervisor. Table
/// <c>mistakes</c> (S-65).
/// </summary>
/// <remarks>
/// <b>Every mistake has a branch</b>, the one where it happened, whoever is
/// responsible for it. <see cref="Responsible"/> then says whether it is put
/// down to the branch as a whole, with no agent, or to one agent, named in
/// <see cref="AgentId"/>. The database holds the two together
/// (<c>ck_mistakes_agent</c>), so a row can never name an agent and blame the
/// branch, or blame an agent and name nobody.
///
/// <b>The customer is optional and kept by number</b> (Dia, 1 Oct 2026). Some
/// branch mistakes have no one customer behind them. When there is one, the
/// number is kept as typed even if nobody has it on file, and linked to the
/// saved customer it belongs to, found the way the pop-up finds a caller (A-13).
/// </remarks>
public class Mistake
{
    public Guid Id { get; set; }

    /// <summary>The restaurant's day it happened on.</summary>
    public DateOnly OccurredOn { get; set; }

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>One of <see cref="Shared.MistakeResponsibilities"/>.</summary>
    public string Responsible { get; set; } = null!;

    /// <summary>Set exactly when <see cref="Responsible"/> is <c>Agent</c>.</summary>
    public Guid? AgentId { get; set; }
    public User? Agent { get; set; }

    /// <summary>What it cost, when it cost something. Null is "no value", not zero.</summary>
    public decimal? Value { get; set; }

    /// <summary>The customer has been compensated for it (تم التعويض). False until the supervisor ticks it (Dia, 3 Oct 2026).</summary>
    public bool Compensated { get; set; }

    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }

    /// <summary>As the supervisor typed it.</summary>
    public string? CustomerNumberRaw { get; set; }

    /// <summary><c>PhoneNormalizer.Normalize</c> of <see cref="CustomerNumberRaw"/>, for the search.</summary>
    public string? CustomerNormalised { get; set; }

    /// <summary>What went wrong. Required: a mistake with no words is a row nobody can act on.</summary>
    public string Notes { get; set; } = null!;

    public Guid? CreatedBy { get; set; }
    public User? Creator { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
