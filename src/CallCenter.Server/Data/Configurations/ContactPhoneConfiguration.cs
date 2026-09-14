using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class ContactPhoneConfiguration : IEntityTypeConfiguration<ContactPhone>
{
    public void Configure(EntityTypeBuilder<ContactPhone> builder)
    {
        builder.ToTable("contact_phones");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Raw).IsRequired();
        builder.Property(x => x.Normalised).IsRequired();
        builder.Property(x => x.IsPrimary).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        // Stored generated column: right(normalised, 9). Written by PostgreSQL,
        // never by the application.
        builder.Property(x => x.Last9)
            .HasComputedColumnSql("right(normalised, 9)", stored: true);

        // Unique on the normalised number - this is what makes duplicate
        // detection (A-63) free.
        builder.HasIndex(x => x.Normalised)
            .IsUnique()
            .HasDatabaseName("ux_contact_phones_normalised");

        // Fuzzy match key for caller lookup (A-13).
        builder.HasIndex(x => x.Last9)
            .HasDatabaseName("ix_contact_phones_last9");

        builder.HasOne(x => x.Contact)
            .WithMany(c => c.Phones)
            .HasForeignKey(x => x.ContactId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
