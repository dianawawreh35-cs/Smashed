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

        builder.HasOne<Communication>().WithMany()
            .HasForeignKey(x => x.CommunicationId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ChangedByUser).WithMany()
            .HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
