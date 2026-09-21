using CallCenter.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NameNormalised).IsRequired().HasMaxLength(200);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.NameNormalised)
            .IsUnique()
            .HasDatabaseName("ux_menu_category_name");
    }
}

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items", t =>
        {
            // Zero is a real price - a free extra - so only negatives are wrong.
            t.HasCheckConstraint("ck_menu_items_price", "price IS NULL OR price >= 0");
            t.HasCheckConstraint("ck_menu_items_meal_price", "meal_price IS NULL OR meal_price >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.NameNormalised).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Price).HasPrecision(10, 2);
        builder.Property(x => x.MealPrice).HasPrecision(10, 2);
        builder.Property(x => x.ImageContentType).HasMaxLength(100);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        // Unique per category, not globally: "بطاطا عادية (صغير)" is one thing,
        // but two categories could each legitimately hold a "كولا".
        builder.HasIndex(x => new { x.CategoryId, x.NameNormalised })
            .IsUnique()
            .HasDatabaseName("ux_menu_item_name_in_category");

        // The agent's search: name first, then the browse-by-category listing.
        builder.HasIndex(x => x.NameNormalised).HasDatabaseName("ix_menu_item_name");
        builder.HasIndex(x => new { x.CategoryId, x.SortOrder }).HasDatabaseName("ix_menu_item_order");

        builder.HasOne(x => x.Category).WithMany(c => c.Items)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
