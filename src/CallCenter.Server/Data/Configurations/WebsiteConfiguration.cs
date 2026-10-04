using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Websites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class WebsiteConfiguration : IEntityTypeConfiguration<Website>
{
    public void Configure(EntityTypeBuilder<Website> builder)
    {
        builder.ToTable("websites", t =>
        {
            t.HasCheckConstraint("ck_websites_login",
                $"login IN ('{WebsiteLogins.Own}','{WebsiteLogins.Shared}')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.NameAr).IsRequired().HasMaxLength(60);
        builder.Property(x => x.NameEn).IsRequired().HasMaxLength(60);
        builder.Property(x => x.Url).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Login).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Username).HasMaxLength(200);
        builder.Property(x => x.CartUrl).HasMaxLength(500);
        builder.Property(x => x.UsernameSelector).HasMaxLength(300);
        builder.Property(x => x.PasswordSelector).HasMaxLength(300);
        builder.Property(x => x.SubmitSelector).HasMaxLength(300);
        builder.Property(x => x.AlertsWithSound).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}
