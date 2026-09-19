using System.IO;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.AgentApp.Data;

/// <summary>
/// The offline buffer: a SQLite database on the laptop holding everything the
/// agent has done that has not reached the server yet (A-04).
/// </summary>
/// <remarks>
/// One file, <c>%LOCALAPPDATA%\CallCenter\agent-buffer.db</c>, shared by every
/// agent who uses the laptop. The rows belong to whoever was signed in when they
/// were made, which is fine because they are only ever replayed to the server —
/// nothing here is shown to an agent, so one agent cannot see another's work
/// (A-05).
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
/// deliberately generic table, so it should never need to change, and shipping a
/// migration runner to every laptop to maintain a queue would be out of
/// proportion. See <see cref="PendingUpload"/> for why the shape holds.
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
}
