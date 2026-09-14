namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A classification type: Order, Cancellation, Complaint, Inquiry, WrongNumber,
/// Other. Table <c>classification_types</c>. Editable by the supervisor (S-40).
/// </summary>
public class ClassificationType
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string LabelAr { get; set; } = null!;

    public string LabelEn { get; set; } = null!;

    /// <summary>Hex colour for charts and badges.</summary>
    public string? Colour { get; set; }

    /// <summary>Order and Complaint are system types and cannot be deleted.</summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Classification> Classifications { get; } = new List<Classification>();
}
