using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class RecordingConfiguration : IEntityTypeConfiguration<Recording>
{
    public void Configure(EntityTypeBuilder<Recording> builder)
    {
        builder.ToTable("recordings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Path).IsRequired();
        builder.Property(x => x.Format).IsRequired().HasDefaultValue("wav");
        builder.Property(x => x.UploadedAt).HasDefaultValueSql("now()");

        // One recording per communication.
        builder.HasIndex(x => x.CommunicationId).IsUnique();

        builder.HasOne(x => x.Communication)
            .WithOne(c => c.Recording)
            .HasForeignKey<Recording>(x => x.CommunicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
