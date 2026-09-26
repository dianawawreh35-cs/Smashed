using System.Collections.Concurrent;
using CallCenter.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Tests;

/// <summary>
/// Removes every row the database tests made. Runs before the first test and
/// after the last one (see <see cref="CallCenterApiFactory"/>).
/// </summary>
/// <remarks>
/// The tests used to leave their rows behind on purpose — each made rows named
/// so they could not collide, and cleaning up was one more thing to get wrong.
/// On 25 Sep the development database had 305 test agents, 143 of their
/// sessions, 186 test calls, 106 test branches, 117 test forms and a hundred
/// "WhatsApp 1a2b3c4d" channels, and the channels were on the Settings page for
/// the supervisor to see. Dia: <i>a lot of things aren't cleaned</i>. So now
/// everything is.
///
/// <b>How a test row is recognised.</b> By the names the tests give them,
/// which were already chosen to be unmistakable: users whose login starts
/// <c>test-</c> or <c>rec-agent-</c>, calls whose SIP Call-ID ends <c>@test</c> or
/// starts <c>rec-</c> and ends <c>@pbx</c> (the recording tests), branches named
/// <c>Test branch</c> plus a suffix, types named <c>TestType</c> or
/// <c>AccessType</c> plus a suffix, channels with an eight-hex-digit suffix,
/// forms with a version at or above 100,000. Contacts are the one thing without
/// a pattern: the tests give them real-looking names and numbers so matching
/// is tested honestly. <see cref="TestData.CreateContactAsync"/> registers the
/// ones it makes, and a contact created through the API by a test user carries
/// that user as <c>created_by</c>.
///
/// <b>Running before the first test too</b> is what makes a crashed run
/// harmless: whatever it left is gone before the next one starts. Anything
/// the sweep cannot name (a contact from a run that crashed before the sweep,
/// made straight through <c>QueryAsync</c>) stays, so tests should make
/// contacts through <see cref="TestData"/>.
///
/// The order is the foreign keys': communications first (classifications,
/// history and recordings cascade from them), then what they point at, then
/// what points at users, then the users.
/// </remarks>
public static class TestSweeper
{
    /// <summary>Contacts the tests made through <see cref="TestData"/>, by id, for this run.</summary>
    public static readonly ConcurrentBag<Guid> Contacts = [];

    public static async Task SweepAsync(CallCenterDbContext db, CancellationToken ct = default)
    {
        var contacts = Contacts.Distinct().ToArray();

        // One connection for the whole sweep: the working tables are temporary,
        // and a pooled connection is reset (DISCARD ALL) each time it is returned.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await SweepOnOpenConnectionAsync(db, contacts, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // The SQL below is raw: EF reads braces as format placeholders, so the
    // regex quantifier {8} is written {{8}}.
    private static async Task SweepOnOpenConnectionAsync(CallCenterDbContext db, Guid[] contacts, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS sweep_comms; DROP TABLE IF EXISTS sweep_contacts; DROP TABLE IF EXISTS sweep_users;
            CREATE TEMP TABLE sweep_users AS
                SELECT id FROM users WHERE login LIKE 'test-%' OR login LIKE 'rec-agent-%';
            """, ct);

        await db.Database.ExecuteSqlAsync(
            $"""
            CREATE TEMP TABLE sweep_contacts AS
                SELECT id FROM contacts
                WHERE id = ANY({contacts})
                   OR created_by IN (SELECT id FROM sweep_users);
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TEMP TABLE sweep_comms AS
                SELECT id FROM communications
                WHERE sip_call_id LIKE '%@test' OR sip_call_id LIKE 'rec-%@pbx' OR sip_call_id LIKE 'never-reported-%@pbx'
                   OR pbx_unique_id LIKE 'issabel:%|test-queue'
                   OR agent_id IN (SELECT id FROM sweep_users)
                   OR contact_id IN (SELECT id FROM sweep_contacts)
                   OR channel_id IN (SELECT id FROM channels WHERE NOT is_system AND name ~ ' [0-9a-f]{{8}}( Business)?$')
                   OR branch_id IN (SELECT id FROM branches WHERE name ~ '^Test branch [0-9a-f]{{8}}$');
            """, ct);

        // Communications and everything hanging off them.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM follow_up_tasks
             WHERE communication_id IN (SELECT id FROM sweep_comms)
                OR closed_by_communication_id IN (SELECT id FROM sweep_comms)
                OR contact_id IN (SELECT id FROM sweep_contacts)
                OR created_by IN (SELECT id FROM sweep_users)
                OR assigned_to IN (SELECT id FROM sweep_users)
                OR closed_by IN (SELECT id FROM sweep_users);
            DELETE FROM communications WHERE id IN (SELECT id FROM sweep_comms);
            """, ct);

        // What the communications pointed at, now that nothing does.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM classification_types t
             WHERE (t.name ~ '^(TestType|AccessType)[0-9a-f]{{8}}$' OR t.name ~ '^Test-[0-9a-f]{{7}}$')
               AND NOT EXISTS (SELECT 1 FROM classifications c WHERE c.type_id = t.id);
            DELETE FROM branches b
             WHERE b.name ~ '^Test branch [0-9a-f]{{8}}$'
               AND NOT EXISTS (SELECT 1 FROM communications m WHERE m.branch_id = b.id)
               AND NOT EXISTS (SELECT 1 FROM delivery_areas d WHERE d.branch_id = b.id);
            DELETE FROM channels ch
             WHERE NOT ch.is_system AND ch.name ~ ' [0-9a-f]{{8}}( Business)?$'
               AND NOT EXISTS (SELECT 1 FROM communications m WHERE m.channel_id = ch.id);
            DELETE FROM form_definitions f
             WHERE f.version >= 100000
               AND NOT f.is_current
               AND NOT EXISTS (SELECT 1 FROM classifications c WHERE c.form_version = f.version);
            DELETE FROM contact_phones WHERE contact_id IN (SELECT id FROM sweep_contacts);
            DELETE FROM contacts c
             WHERE c.id IN (SELECT id FROM sweep_contacts)
               AND NOT EXISTS (SELECT 1 FROM communications m WHERE m.contact_id = c.id)
               AND NOT EXISTS (SELECT 1 FROM contacts o WHERE o.merged_into_id = c.id);
            """, ct);

        // What points at a test user and has to stay: a form a test supervisor
        // published through the API is a real version (it may be current), so
        // it keeps its place and loses its author. The same for anything else a
        // test account touched that is not itself a test row.
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE form_definitions SET created_by = NULL WHERE created_by IN (SELECT id FROM sweep_users);
            UPDATE contacts SET updated_by = NULL WHERE updated_by IN (SELECT id FROM sweep_users);
            UPDATE contacts SET flag_changed_by = NULL WHERE flag_changed_by IN (SELECT id FROM sweep_users);
            UPDATE contacts SET created_by = NULL WHERE created_by IN (SELECT id FROM sweep_users);
            UPDATE settings SET updated_by = NULL WHERE updated_by IN (SELECT id FROM sweep_users);
            UPDATE delivery_areas SET created_by = NULL WHERE created_by IN (SELECT id FROM sweep_users);
            UPDATE delivery_areas SET updated_by = NULL WHERE updated_by IN (SELECT id FROM sweep_users);
            UPDATE menu_items SET created_by = NULL WHERE created_by IN (SELECT id FROM sweep_users);
            UPDATE menu_items SET updated_by = NULL WHERE updated_by IN (SELECT id FROM sweep_users);
            UPDATE classifications SET updated_by = NULL WHERE updated_by IN (SELECT id FROM sweep_users);
            UPDATE classifications SET resolved_by = NULL WHERE resolved_by IN (SELECT id FROM sweep_users);
            DELETE FROM audit_log WHERE user_id IN (SELECT id FROM sweep_users);
            DELETE FROM agent_sessions WHERE user_id IN (SELECT id FROM sweep_users);
            DELETE FROM outbox_sync WHERE user_id IN (SELECT id FROM sweep_users);
            """, ct);

        // A test user who classified a real call is not a row this can remove;
        // the user then stays, and the next sweep tries again. It has not
        // happened, because every test classifies its own calls.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM users u
             WHERE u.id IN (SELECT id FROM sweep_users)
               AND NOT EXISTS (SELECT 1 FROM classifications c WHERE c.classified_by = u.id)
               AND NOT EXISTS (SELECT 1 FROM classification_history h WHERE h.changed_by = u.id);
            DROP TABLE sweep_comms; DROP TABLE sweep_contacts; DROP TABLE sweep_users;
            """, ct);
    }
}
