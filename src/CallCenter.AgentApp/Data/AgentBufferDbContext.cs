using System.IO;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.AgentApp.Data;

/// <summary>
/// The offline buffer: a SQLite database on the laptop holding everything the
/// agent has done that has not reached the server yet (A-04).
/// </summary>
/// <remarks>
/// One file, <c>%LOCALAPPDATA%\CallCenter\agent-buffer.db</c>, shared by every
/// agent who uses the laptop. Each row carries the id of whoever was signed in
/// when it was made, and is replayed only while they are signed in again
/// (F-03). Before 27 Sep it was replayed as whoever signed in next, which filed
/// one agent's offline evening under the next morning's agent. Rows another
/// agent left wait for them; only the count of an agent's own rows that could
/// not be sent is ever shown (A-05).
///
/// <b>Why SQLite and not the file this replaced.</b> The queue started as one
/// JSON object per line, because a list of calls needs nothing more and the
/// SQLite native library was pinned to a version carrying CVE-2025-6965. That
/// advisory is now fixed by pinning 2.1.12, and the buffer is about to hold
/// classifications as well as calls — two kinds of row that have to arrive in
/// order, and where a call and its classification should be removed together or
/// not at all. A file cannot promise either. That is what a database is for.
///
/// Created with <c>EnsureCreated</c> rather than migrations: the schema is one
/// deliberately generic table, so it rarely needs to change, and shipping a
/// migration runner to every laptop to maintain a queue would be out of
/// proportion. See <see cref="PendingUpload"/> for why the shape holds, and
/// <see cref="UpgradeAsync"/> for the one change it has had.
/// </remarks>
public class AgentBufferDbContext(DbContextOptions<AgentBufferDbContext> options) : DbContext(options)
{
    /// <summary>Where the buffer lives on this laptop.</summary>
    public static string DatabasePath { get; } =
        Path.Combine(App.AppDataDirectory, "agent-buffer.db");

    public DbSet<PendingUpload> PendingUploads => Set<PendingUpload>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var upload = builder.Entity<PendingUpload>();

        upload.ToTable("pending_uploads");
        upload.HasKey(x => x.Id);
        upload.Property(x => x.Id).ValueGeneratedOnAdd();

        upload.Property(x => x.Kind).IsRequired();
        upload.Property(x => x.Payload).IsRequired();

        // Replay order. Explicit rather than relying on insertion order, which
        // SQLite does not promise once rows have been deleted from the middle.
        upload.HasIndex(x => x.Id).HasDatabaseName("ix_pending_order");

        // Finding a call's own queued classification while both are waiting.
        upload.HasIndex(x => x.Reference).HasDatabaseName("ix_pending_reference");
    }

    /// <summary>
    /// The columns added since the first version (F-03, F-08), on a buffer
    /// that already exists. <c>EnsureCreated</c> makes a new file with them and
    /// leaves an old one as it is, so an old one is brought up to date here.
    /// </summary>
    /// <remarks>
    /// Hand-written <c>ALTER TABLE</c> rather than EF migrations, for the
    /// reason the table is generic at all: no migration runner on the laptops.
    /// Adding a nullable column keeps every row, so a laptop that was offline
    /// when it was updated loses nothing. Safe to run on every start: a column
    /// already there is skipped.
    /// </remarks>
    public async Task UpgradeAsync(CancellationToken ct = default)
    {
        var columns = await Database
            .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('pending_uploads')")
            .ToListAsync(ct);

        foreach (var (column, type) in new[] { ("UserId", "TEXT"), ("SetAsideAt", "TEXT") })
        {
            if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                // The names are constants above, never input.
#pragma warning disable EF1002
                await Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE pending_uploads ADD COLUMN \"{column}\" {type} NULL", ct);
#pragma warning restore EF1002
            }
        }
    }
}
