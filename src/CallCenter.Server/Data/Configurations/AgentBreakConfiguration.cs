using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class AgentBreakConfiguration : IEntityTypeConfiguration<AgentBreak>
{
    public void Configure(EntityTypeBuilder<AgentBreak> builder)
    {
        builder.ToTable("agent_breaks", t =>
        {
            t.HasCheckConstraint("ck_agent_breaks_ended_by",
                $"ended_by IN ({string.Join(',', BreakEndings.All.Select(e => $"'{e}'"))})");

            // A-86: an end has a reason and a reason has an end, and a break
            // never ends before it began.
            t.HasCheckConstraint("ck_agent_breaks_end",
                "(ended_at IS NULL) = (ended_by IS NULL) AND (ended_at IS NULL OR ended_at >= started_at)");
        });

        builder.HasKey(x => x.Id);

        // Made on the laptop, never by the database.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        // An agent's breaks by time: today's total, and the report's period.
        builder.HasIndex(x => new { x.UserId, x.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_agent_breaks_user");

        // The report and the monitor read every agent's breaks in a period.
        builder.HasIndex(x => x.StartedAt).HasDatabaseName("ix_agent_breaks_started");

        builder.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Session).WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
