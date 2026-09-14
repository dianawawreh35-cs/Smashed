using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A communication channel: Phone, WhatsApp, Facebook, Instagram, Wheels, ...
/// Table <c>channels</c>. Managed by the supervisor (S-41).
/// </summary>
public class Channel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Phone is a system channel and cannot be deleted.</summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Communication> Communications { get; } = new List<Communication>();
}
