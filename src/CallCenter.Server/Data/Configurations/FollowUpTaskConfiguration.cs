using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallCenter.Server.Data.Configurations;

public class FollowUpTaskConfiguration : IEntityTypeConfiguration<FollowUpTask>
{
    public void Configure(EntityTypeBuilder<FollowUpTask> builder)
    {
        builder.ToTable("follow_up_tasks", t =>
        {
            t.HasCheckConstraint("ck_follow_up_tasks_status", In("status", TaskStatuses.All));
            t.HasCheckConstraint("ck_follow_up_tasks_created_from", In("created_from", TaskOrigins.All));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired();
        builder.Property(x => x.Status).IsRequired().HasDefaultValue(TaskStatuses.Open);
        builder.Property(x => x.CreatedFrom).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        // Partial index: the open-tasks list is the only one anybody queries.
        builder.HasIndex(x => new { x.Status, x.DueAt })
            .HasFilter($"status = '{TaskStatuses.Open}'")
            .HasDatabaseName("ix_tasks_open");

        builder.HasIndex(x => x.ContactId).HasDatabaseName("ix_tasks_contact");

        builder.HasOne(x => x.Contact).WithMany()
            .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Communication).WithMany()
            .HasForeignKey(x => x.CommunicationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ClosedByCommunication).WithMany()
            .HasForeignKey(x => x.ClosedByCommunicationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedToUser).WithMany()
            .HasForeignKey(x => x.AssignedTo).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => x.ClosedBy).OnDelete(DeleteBehavior.Restrict);
    }

    private static string In(string column, IReadOnlyList<string> allowed) =>
        $"{column} IN ({string.Join(",", allowed.Select(v => $"'{v}'"))})";
}
