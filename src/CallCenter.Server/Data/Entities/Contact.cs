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

    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set when this contact was merged into another (soft delete).</summary>
    public Guid? MergedIntoId { get; set; }
    public Contact? MergedInto { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ContactPhone> Phones { get; } = new List<ContactPhone>();
    public ICollection<Communication> Communications { get; } = new List<Communication>();
}
