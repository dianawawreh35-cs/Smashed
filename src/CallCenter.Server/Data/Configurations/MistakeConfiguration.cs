using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class MistakeConfiguration : IEntityTypeConfiguration<Mistake>
{
    public void Configure(EntityTypeBuilder<Mistake> builder)
    {
        builder.ToTable("mistakes", t =>
        {
            t.HasCheckConstraint("ck_mistakes_responsible",
                $"responsible IN ('{MistakeResponsibilities.Branch}','{MistakeResponsibilities.Agent}')");

            // S-65: an agent is named exactly when the mistake is the agent's.
            t.HasCheckConstraint("ck_mistakes_agent",
                $"(responsible = '{MistakeResponsibilities.Agent}') = (agent_id IS NOT NULL)");

            t.HasCheckConstraint("ck_mistakes_value", "value IS NULL OR value >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Responsible).IsRequired();
        builder.Property(x => x.Value).HasPrecision(10, 2);
        builder.Property(x => x.CustomerNumberRaw).HasMaxLength(32);
        builder.Property(x => x.CustomerNormalised).HasMaxLength(32);
        builder.Property(x => x.Notes).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        // The page lists them newest day first, a period at a time.
        builder.HasIndex(x => x.OccurredOn).HasDatabaseName("ix_mistakes_occurred");

        builder.HasOne(x => x.Branch).WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Agent).WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Contact).WithMany()
            .HasForeignKey(x => x.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Creator).WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
