using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class CommunicationConfiguration : IEntityTypeConfiguration<Communication>
{
    public void Configure(EntityTypeBuilder<Communication> builder)
    {
        builder.ToTable("communications", t =>
        {
            t.HasCheckConstraint("ck_communications_kind", In("kind", CommunicationKinds.All));
            t.HasCheckConstraint("ck_communications_direction", In("direction", Directions.All));
            t.HasCheckConstraint("ck_communications_status", In("status", CommunicationStatuses.All));
            t.HasCheckConstraint("ck_communications_source", In("source", CommunicationSources.All));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).IsRequired();
        builder.Property(x => x.Direction).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.Source).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.StartedAt)
            .IsDescending()
            .HasDatabaseName("ix_comm_started");

        builder.HasIndex(x => new { x.AgentId, x.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_comm_agent_started");

        builder.HasIndex(x => new { x.ContactId, x.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_comm_contact");

        builder.HasIndex(x => x.RemoteNormalised).HasDatabaseName("ix_comm_remote");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_comm_status");

        // Partial unique indexes: one row per PBX call, one per SIP call leg.
        builder.HasIndex(x => x.PbxUniqueId)
            .IsUnique()
            .HasFilter("pbx_unique_id IS NOT NULL")
            .HasDatabaseName("ux_comm_pbx_unique");

        builder.HasIndex(x => new { x.SipCallId, x.Extension })
            .IsUnique()
            .HasFilter("sip_call_id IS NOT NULL")
            .HasDatabaseName("ux_comm_sip_call");

        builder.HasOne(x => x.Channel).WithMany(c => c.Communications)
            .HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Agent).WithMany()
            .HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Contact).WithMany(c => c.Communications)
            .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch).WithMany(b => b.Communications)
            .HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        // S-55: a ring points at the abandoned call it belonged to. Deleting
        // that call (the demo-data removal, say) only undoes the link.
        builder.HasOne(x => x.AbandonedCall).WithMany()
            .HasForeignKey(x => x.AbandonedCallId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.AbandonedCallId)
            .HasFilter("abandoned_call_id IS NOT NULL")
            .HasDatabaseName("ix_comm_abandoned_call");
    }

    /// <summary>Renders a CHECK constraint body: <c>col IN ('a','b')</c>.</summary>
    private static string In(string column, IReadOnlyList<string> allowed) =>
        $"{column} IN ({string.Join(",", allowed.Select(v => $"'{v}'"))})";
}
