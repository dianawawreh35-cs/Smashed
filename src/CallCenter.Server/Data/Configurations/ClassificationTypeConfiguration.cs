using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class ClassificationTypeConfiguration : IEntityTypeConfiguration<ClassificationType>
{
    public void Configure(EntityTypeBuilder<ClassificationType> builder)
    {
        builder.ToTable("classification_types");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.LabelAr).IsRequired();
        builder.Property(x => x.LabelEn).IsRequired();
        builder.Property(x => x.IsSystem).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
