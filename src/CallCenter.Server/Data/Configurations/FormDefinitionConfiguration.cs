using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable("form_definitions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Definition).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Direction).HasMaxLength(10).HasDefaultValue(Directions.In);
        builder.Property(x => x.IsCurrent).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.Version).IsUnique();

        // Exactly one current version per direction. A partial unique index
        // means a second current row for the same direction is rejected by the
        // database.
        builder.HasIndex(x => x.Direction)
            .IsUnique()
            .HasFilter("is_current")
            .HasDatabaseName("ux_form_current");

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
