using CallCenter.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class DeliveryAreaConfiguration : IEntityTypeConfiguration<DeliveryArea>
{
    public void Configure(EntityTypeBuilder<DeliveryArea> builder)
    {
        builder.ToTable("delivery_areas", t =>
            // Zero is allowed — three areas beside the Rafat branch deliver
            // free — but a negative price is somebody's typo.
            t.HasCheckConstraint("ck_delivery_areas_price", "price >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NameNormalised).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Price).HasPrecision(10, 2);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        // One area, one branch. "Which branch delivers here?" must have one
        // answer, so the name is unique on its own rather than per branch.
        builder.HasIndex(x => x.NameNormalised)
            .IsUnique()
            .HasDatabaseName("ux_delivery_area_name");

        // The supervisor's screen lists a branch's areas together.
        builder.HasIndex(x => x.BranchId).HasDatabaseName("ix_delivery_area_branch");

        builder.HasOne(x => x.Branch).WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // created_by / updated_by reference users(id) but are not navigations:
        // the rows outlive the supervisor who typed them.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
