using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class PbxEventRawConfiguration : IEntityTypeConfiguration<PbxEventRaw>
{
    public void Configure(EntityTypeBuilder<PbxEventRaw> builder)
    {
        builder.ToTable("pbx_events_raw", t => t.HasCheckConstraint(
            "ck_pbx_events_raw_source",
            $"source IN ({string.Join(",", PbxEventSources.All.Select(v => $"'{v}'"))})"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();

        builder.Property(x => x.ReceivedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.Source).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();

        // The retention job deletes by age, so this index is what makes it cheap.
        builder.HasIndex(x => x.ReceivedAt).HasDatabaseName("ix_pbx_events_received");
    }
}
