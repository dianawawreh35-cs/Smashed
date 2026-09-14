using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class AgentSessionConfiguration : IEntityTypeConfiguration<AgentSession>
{
    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.ToTable("agent_sessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LaptopId).IsRequired();
        builder.Property(x => x.LoggedInAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.UserId, x.LoggedInAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_sessions_user");

        builder.HasOne(x => x.User).WithMany(u => u.Sessions)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
