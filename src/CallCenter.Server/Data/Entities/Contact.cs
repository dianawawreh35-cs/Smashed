using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A customer. Table <c>contacts</c>. Shared by every agent (A-61) and never
/// hard-deleted — a merged contact keeps <see cref="MergedIntoId"/> and
/// <see cref="DeletedAt"/> instead.
/// </summary>
public class Contact
{
    public Guid Id { get; set; }

    /// <summary>Nullable: a bare number can be flagged before it has a name (S-45).</summary>
    public string? Name { get; set; }

    /// <summary>
    /// <see cref="Name"/> reduced for comparison by
    /// <c>CallCenter.Shared.Text.NameNormalizer</c> — hamza forms folded,
    /// diacritics stripped, case and spacing levelled. Matching compares this;
    /// nothing ever displays it. Written by the application on every save.
    /// </summary>
    public string? NameNormalised { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }

    /// <summary>Gate code, landmark — kept apart from general notes.</summary>
    public string? DeliveryNotes { get; set; }

    /// <summary>Set by the supervisor only (S-45); agents see it but cannot change it.</summary>
    public bool IsVip { get; set; }

    /// <summary>Calls from a blocked number are rejected by the Agent App (A-17).</summary>
    public bool IsBlocked { get; set; }

    public string? FlagReason { get; set; }
    public Guid? FlagChangedBy { get; set; }
    public DateTimeOffset? FlagChangedAt { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Where the contact came from, one of <see cref="ContactSources"/>. R-16
    /// counts every contact as new in the period it was made, except the old
    /// system's customer book, which was there before anybody rang.
    /// </summary>
    public string Source { get; set; } = ContactSources.Agent;

    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set when this contact was merged into another (soft delete).</summary>
    public Guid? MergedIntoId { get; set; }
    public Contact? MergedInto { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ContactPhone> Phones { get; } = new List<ContactPhone>();
    public ICollection<Communication> Communications { get; } = new List<Communication>();
}

/// <summary><c>contacts.source</c>: where a contact came from (R-16, F-10 of the 27 Sep review).</summary>
/// <remarks>
/// It was inferred from <c>created_by</c> being empty, which was meant to pick
/// out the seed but also caught every contact the POS lookup made (A-67), so
/// once the lookup was on, "new customers" drifted towards zero.
/// </remarks>
public static class ContactSources
{
    /// <summary>The old ordering system's customer book, loaded by <c>seed</c>. Never new.</summary>
    public const string Seed = "Seed";

    /// <summary>Saved by a person, an agent or a supervisor, from either app.</summary>
    public const string Agent = "Agent";

    /// <summary>Made by the POS customer lookup for a caller it knew (A-67).</summary>
    public const string Pos = "Pos";

    public static readonly IReadOnlyList<string> All = [Seed, Agent, Pos];
}
