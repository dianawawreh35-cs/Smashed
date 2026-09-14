using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class OutboxSyncConfiguration : IEntityTypeConfiguration<OutboxSync>
{
    public void Configure(EntityTypeBuilder<OutboxSync> builder)
    {
        builder.ToTable("outbox_sync");

        // The laptop-generated id is the key, which is what makes replaying a
        // queued batch idempotent.
        builder.HasKey(x => x.ClientOpId);
        builder.Property(x => x.ClientOpId).ValueGeneratedNever();

        builder.Property(x => x.AppliedAt).HasDefaultValueSql("now()");

        builder.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
