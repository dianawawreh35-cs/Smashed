using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class ClassificationConfiguration : IEntityTypeConfiguration<Classification>
{
    public void Configure(EntityTypeBuilder<Classification> builder)
    {
        builder.ToTable("classifications");

        // Keyed by the communication: at most one classification each.
        builder.HasKey(x => x.CommunicationId);
        builder.Property(x => x.CommunicationId).ValueGeneratedNever();

        builder.Property(x => x.OrderValue).HasColumnType("numeric(10,2)");
        builder.Property(x => x.FollowUp).HasDefaultValue(false);
        builder.Property(x => x.CustomValues)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.ClassifiedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.TypeId).HasDatabaseName("ix_class_type");

        // GIN over the jsonb, so filtering on a custom field stays fast.
        builder.HasIndex(x => x.CustomValues)
            .HasMethod("gin")
            .HasDatabaseName("ix_class_custom");

        builder.HasOne(x => x.Communication)
            .WithOne(c => c.Classification)
            .HasForeignKey<Classification>(x => x.CommunicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Type).WithMany(t => t.Classifications)
            .HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);

        // form_version references form_definitions(version), a unique column
        // that is not that table's primary key.
        builder.HasOne(x => x.Form).WithMany()
            .HasForeignKey(x => x.FormVersion)
            .HasPrincipalKey(f => f.Version)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ClassifiedByUser).WithMany()
            .HasForeignKey(x => x.ClassifiedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.ResolvedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
