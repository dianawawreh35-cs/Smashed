using System.Text.Json;
using CallCenter.Shared;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// One version of the classification form. Table <c>form_definitions</c>.
/// </summary>
/// <remarks>
/// Editing the form (S-40) creates a new version and moves <see cref="IsCurrent"/>
/// to it; existing classifications keep the version they were captured under, so
/// their history renders with the fields that existed at the time. Only one row
/// per <see cref="Direction"/> may have <see cref="IsCurrent"/> true, enforced by
/// a partial unique index.
/// </remarks>
public class FormDefinition
{
    public Guid Id { get; set; }

    /// <summary>Unique, and the target of <see cref="Classification.FormVersion"/>.</summary>
    public int Version { get; set; }

    /// <summary>The field list, as jsonb. See docs/SCHEMA.md section 4 for the shape.</summary>
    public JsonDocument Definition { get; set; } = null!;

    /// <summary>
    /// Which calls this form is for: <see cref="Directions.In"/> or
    /// <see cref="Directions.Out"/>. An outbound call is a different
    /// conversation from an inbound one, so each direction has its own form.
    /// </summary>
    public string Direction { get; set; } = Directions.In;

    public bool IsCurrent { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
