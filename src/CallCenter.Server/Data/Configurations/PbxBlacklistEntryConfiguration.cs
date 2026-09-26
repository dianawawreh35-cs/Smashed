using CallCenter.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class PbxBlacklistEntryConfiguration : IEntityTypeConfiguration<PbxBlacklistEntry>
{
    public void Configure(EntityTypeBuilder<PbxBlacklistEntry> builder)
    {
        builder.ToTable("pbx_blacklist", t =>
            // Digits only: this is what is keyed into the PBX on a phone pad.
            t.HasCheckConstraint("ck_pbx_blacklist_number", "number ~ '^[0-9]+$'"));

        builder.HasKey(x => x.Number);

        builder.Property(x => x.Number).HasMaxLength(32);
        builder.Property(x => x.OnPbx).HasDefaultValue(false);
        builder.Property(x => x.FailedAttempts).HasDefaultValue(0);
    }
}
