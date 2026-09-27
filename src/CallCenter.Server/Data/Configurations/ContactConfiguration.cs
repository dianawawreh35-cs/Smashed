using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts", t =>
            t.HasCheckConstraint("ck_contacts_source", In("source", ContactSources.All)));
        builder.HasKey(x => x.Id);

        // A default in the database too, so a row inserted by hand (tools/demo-data)
        // is a person's, as the application's own inserts are.
        builder.Property(x => x.Source).HasMaxLength(10).IsRequired().HasDefaultValue(ContactSources.Agent);

        builder.Property(x => x.IsVip).HasDefaultValue(false);
        builder.Property(x => x.IsBlocked).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        // A merged contact points at the contact it was merged into.
        builder.HasOne(x => x.MergedInto)
            .WithMany()
            .HasForeignKey(x => x.MergedIntoId)
            .OnDelete(DeleteBehavior.Restrict);

        // created_by / updated_by / flag_changed_by reference users(id) but are
        // not navigations - the rows outlive the user who touched them.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.FlagChangedBy).OnDelete(DeleteBehavior.Restrict);

        // What the duplicate-name warning compares (A-63). Indexed because it
        // is looked up on every contact save.
        builder.HasIndex(x => x.NameNormalised)
            .HasDatabaseName("ix_contacts_name_normalised");

        // Full-text search over name + address (A-61). Created in the migration
        // as raw SQL - EF cannot express a GIN index over an expression.
    }

    /// <summary>Renders a CHECK constraint body: <c>col IN ('a','b')</c>.</summary>
    private static string In(string column, IReadOnlyList<string> allowed) =>
        $"{column} IN ({string.Join(",", allowed.Select(v => $"'{v}'"))})";
}
