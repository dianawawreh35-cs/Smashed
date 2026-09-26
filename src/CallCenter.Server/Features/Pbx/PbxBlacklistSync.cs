using System.Globalization;
using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Pbx;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Pbx;

/// <summary>One sync at a time, whichever timer tick started it (S-46).</summary>
public sealed class PbxBlacklistGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

/// <summary>
/// Keeps the PBX's blacklist in step with the Blocked flag (S-46): a number
/// that is blocked is added with <c>*30</c>, and one the server added that is
/// no longer blocked is removed with <c>*31</c>.
/// </summary>
/// <remarks>
/// <b>A comparison, not a queue of instructions.</b> Each run works out which
/// numbers ought to be on the PBX — every number of every blocked contact —
/// and compares that with <c>pbx_blacklist</c>, what the PBX has been told. So
/// it does not matter how a number came to be blocked or unblocked: a flag set,
/// a number added to a blocked contact, two contacts merged, a contact deleted.
/// A failed call is simply still different next time, and is tried again after
/// <see cref="RetryAfter"/>.
///
/// <b>The number keyed in is the local form,</b> <c>0599123456</c>: a phone pad
/// has no <c>+</c>, and the PBX's own reports show callers that way. A number
/// with no local form — an extension, a foreign number — is not sent.
///
/// <b>Only what the server added is removed.</b> A number put on the PBX's
/// blacklist by hand has no row in <c>pbx_blacklist</c>, so it is never taken
/// off, however its contact is flagged.
///
/// <b>The extension it dials from is shared</b> with the queue switch (S-60),
/// and is <see cref="PbxFeatureLine"/>'s. It is entered on this feature's card
/// because the blacklist came first.
/// </remarks>
public class PbxBlacklistSync(
    CallCenterDbContext db,
    IPbxFeatureDialer dialer,
    PbxFeatureLine line,
    ISipSecretProtector protector,
    PbxBlacklistGate gate,
    TimeProvider clock,
    ILogger<PbxBlacklistSync> logger)
{
    /// <summary>How long after a failed call the number is tried again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>How many failures the settings screen lists.</summary>
    public const int FailuresShown = 20;

    public static class Keys
    {
        public const string LastSucceededAt = "pbx.blacklist.last_succeeded_at";

        public const string Prefix = "pbx.blacklist.";
    }

    // ---- settings ---------------------------------------------------------------

    public async Task<PbxBlacklistDto> StatusAsync(CancellationToken ct = default)
    {
        var stored = await LoadAsync(Keys.Prefix, ct);
        var (extension, secretSet) = await line.StoredAsync(ct);
        var wanted = await WantedAsync(ct);
        var rows = await db.PbxBlacklist.AsNoTracking().ToListAsync(ct);
        var onPbx = rows.Where(r => r.OnPbx).Select(r => r.Number).ToHashSet();

        var failures = rows
            .Where(r => r.LastError != null && r.LastAttemptAt != null)
            .OrderByDescending(r => r.LastAttemptAt)
            .Take(FailuresShown)
            .Select(r => new PbxBlacklistFailureDto(r.Number, !r.OnPbx, r.LastError!, r.FailedAttempts, r.LastAttemptAt!.Value))
            .ToList();

        return new PbxBlacklistDto(
            extension,
            secretSet,
            await line.GetAsync(ct) is not null,
            onPbx.Count,
            wanted.Count(n => !onPbx.Contains(n)) + onPbx.Count(n => !wanted.Contains(n)),
            failures,
            DateTimeOffset.TryParse(stored.GetValueOrDefault(Keys.LastSucceededAt), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var at) ? at : null);
    }

    /// <summary>Saves the extension and its password (<see cref="PbxFeatureLine"/>, shared with the queue switch).</summary>
    /// <returns>The problems found, by field; empty when saved.</returns>
    public async Task<IReadOnlyDictionary<string, string>> SaveAsync(
        UpdatePbxBlacklistRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var problems = new Dictionary<string, string>();
        var extension = (request.Extension ?? string.Empty).Trim();

        if (extension.Length > 0 && !extension.All(char.IsAsciiDigit))
        {
            problems["extension"] = "must be the extension's number, digits only";
            return problems;
        }

        var stored = await LoadAsync(PbxFeatureLine.Keys.Prefix, ct);
        var values = new Dictionary<string, string> { [PbxFeatureLine.Keys.Extension] = extension };

        if (!string.IsNullOrEmpty(request.Secret))
        {
            values[PbxFeatureLine.Keys.Secret] = protector.Protect(request.Secret)!;
        }

        var changed = values
            .Where(v => stored.GetValueOrDefault(v.Key) != v.Value)
            .Select(v => v.Key)
            .ToList();

        if (changed.Count == 0)
        {
            return problems;
        }

        // Never the password, in either direction: only that it changed.
        string? Shown(string key, string? value) => key == PbxFeatureLine.Keys.Secret ? (value is null ? null : "(set)") : value;

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            Entity = "settings",
            Action = "update",
            Before = JsonSerializer.SerializeToDocument(changed.ToDictionary(k => k, k => Shown(k, stored.GetValueOrDefault(k)))),
            After = JsonSerializer.SerializeToDocument(changed.ToDictionary(k => k, k => Shown(k, values[k]))),
        });

        await WriteAsync(changed.ToDictionary(k => k, k => values[k]), actingUserId, ct);

        // New login, new chance: whatever failed with the old one is due now.
        await db.PbxBlacklist
            .Where(r => r.LastError != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastAttemptAt, (DateTimeOffset?)null), ct);

        logger.LogInformation("PBX blacklist settings changed: {Keys}", string.Join(", ", changed));
        return problems;
    }

    /// <summary>The Try again button: every failed number is due at the next run, not after <see cref="RetryAfter"/>.</summary>
    public async Task RetryNowAsync(CancellationToken ct = default) =>
        await db.PbxBlacklist
            .Where(r => r.LastError != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastAttemptAt, (DateTimeOffset?)null), ct);

    // ---- the sync ---------------------------------------------------------------

    /// <summary>
    /// One pass: dials every number that is due, one after another. Returns how
    /// many calls it made. Does nothing when the extension is not set up, or
    /// while another pass is still dialling.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!await gate.Lock.WaitAsync(0, ct))
        {
            return 0;
        }

        try
        {
            if (await line.GetAsync(ct) is not { } extension)
            {
                return 0;
            }

            var wanted = await WantedAsync(ct);

            // A number that failed to go on and has since been unblocked has
            // nothing left to do.
            await db.PbxBlacklist
                .Where(r => !r.OnPbx && !wanted.Contains(r.Number))
                .ExecuteDeleteAsync(ct);

            var rows = await db.PbxBlacklist.ToListAsync(ct);
            var plan = Plan(wanted, rows, clock.GetUtcNow());

            foreach (var (number, action) in plan)
            {
                ct.ThrowIfCancellationRequested();
                await DialAsync(extension, number, action, rows.FirstOrDefault(r => r.Number == number), ct);
            }

            return plan.Count;
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    /// <summary>
    /// What to dial, and in which order: additions first — a nuisance caller
    /// waiting to be blocked matters more than one waiting to be let back — and
    /// nothing that failed less than <see cref="RetryAfter"/> ago.
    /// </summary>
    public static IReadOnlyList<(string Number, BlacklistAction Action)> Plan(
        IReadOnlySet<string> wanted, IReadOnlyList<PbxBlacklistEntry> rows, DateTimeOffset now)
    {
        var byNumber = rows.ToDictionary(r => r.Number);

        bool Due(PbxBlacklistEntry? row) =>
            row?.LastError is null || row.LastAttemptAt is not { } last || now - last >= RetryAfter;

        var adds = wanted
            .Where(n => !(byNumber.TryGetValue(n, out var row) && row.OnPbx))
            .Where(n => Due(byNumber.GetValueOrDefault(n)))
            .Order(StringComparer.Ordinal)
            .Select(n => (n, BlacklistAction.Add));

        var removes = rows
            .Where(r => r.OnPbx && !wanted.Contains(r.Number) && Due(r))
            .Select(r => r.Number)
            .Order(StringComparer.Ordinal)
            .Select(n => (n, BlacklistAction.Remove));

        return adds.Concat(removes).ToList();
    }

    /// <summary>
    /// The number as it is keyed into the PBX: the local form, digits only.
    /// Null for a number that has none — an extension or a foreign number.
    /// </summary>
    public static string? PbxNumber(string normalised) => PhoneNormalizer.ToNational(normalised);

    private async Task DialAsync(
        PbxExtension extension, string number, BlacklistAction action, PbxBlacklistEntry? row, CancellationToken ct)
    {
        string? error = null;

        try
        {
            await dialer.BlacklistAsync(extension, action, number, ct);
        }
        catch (PbxFeatureException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A socket that would not open, or the like. Still the PBX's
            // number not changing, and still worth another try.
            logger.LogError(ex, "PBX blacklist: the call for {Number} failed unexpectedly", number);
            error = $"The call could not be made: {ex.Message}";
        }

        if (row is null)
        {
            row = new PbxBlacklistEntry { Number = number };
            db.PbxBlacklist.Add(row);
        }

        // After the call, which takes half a minute: the moment the PBX had it.
        var now = clock.GetUtcNow();
        row.LastAttemptAt = now;

        if (error is not null)
        {
            logger.LogWarning("PBX blacklist: {Action} {Number} failed: {Reason}", action, number, error);
            row.LastError = error;
            row.FailedAttempts++;
        }
        else if (action == BlacklistAction.Add)
        {
            row.OnPbx = true;
            row.AddedAt = now;
            row.LastError = null;
            row.FailedAttempts = 0;
        }
        else
        {
            db.PbxBlacklist.Remove(row);
        }

        await db.SaveChangesAsync(ct);

        if (error is null)
        {
            await WriteAsync(new Dictionary<string, string>
            {
                [Keys.LastSucceededAt] = clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            }, null, ct);
        }
    }

    /// <summary>Every number that ought to be on the PBX: those of blocked contacts that are still in use.</summary>
    private async Task<HashSet<string>> WantedAsync(CancellationToken ct)
    {
        var normalised = await db.Contacts
            .Where(c => c.IsBlocked && c.DeletedAt == null && c.MergedIntoId == null)
            .SelectMany(c => c.Phones.Select(p => p.Normalised))
            .Distinct()
            .ToListAsync(ct);

        return normalised
            .Select(PbxNumber)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    // ---- storage ----------------------------------------------------------------

    private async Task<Dictionary<string, string>> LoadAsync(string prefix, CancellationToken ct) =>
        await db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    private async Task WriteAsync(IReadOnlyDictionary<string, string> values, Guid? by, CancellationToken ct)
    {
        var keys = values.Keys.ToList();
        var rows = await db.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        var now = clock.GetUtcNow();

        foreach (var (key, value) in values)
        {
            if (!rows.TryGetValue(key, out var row))
            {
                row = new Setting { Key = key };
                db.Settings.Add(row);
            }

            row.Value = value;
            row.UpdatedAt = now;
            if (by is not null) row.UpdatedBy = by;
        }

        await db.SaveChangesAsync(ct);
    }
}
