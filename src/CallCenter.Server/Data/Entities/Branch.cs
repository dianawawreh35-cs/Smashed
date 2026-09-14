using System.ComponentModel.DataAnnotations.Schema;

namespace CallCenter.Server.Data.Entities;

/// <summary>One of the restaurant's branches. Table <c>branches</c>.</summary>
public class Branch
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Communication> Communications { get; } = new List<Communication>();
}
