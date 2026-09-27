using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class ClassificationHistoryConfiguration : IEntityTypeConfiguration<ClassificationHistory>
{
    public void Configure(EntityTypeBuilder<ClassificationHistory> builder)
    {
        builder.ToTable("classification_history");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChangedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.Before).HasColumnType("jsonb");
        builder.Property(x => x.After).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.CommunicationId).HasDatabaseName("ix_class_hist_comm");

        // Restrict, not cascade (M-D05, 27 Sep 2026): this is the audit trail
        // (A-43, N-06). Deleting a call used to take its history with it, so
        // the one record that could show what happened went with the thing it
        // was a record of. Nothing in the application deletes a call; a script
        // that does must delete the history itself, on purpose.
        builder.HasOne<Communication>().WithMany()
            .HasForeignKey(x => x.CommunicationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChangedByUser).WithMany()
            .HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
