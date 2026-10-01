using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Contacts;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Pos;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.Pos;

/// <summary>
/// Puts a name to recent callers from the restaurant POS (A-67): creates the
/// contact for a caller nobody has on file, fills in what an existing contact
/// is missing, and attaches their calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who is asked about.</b> Every number that called in the last
/// <see cref="PosLookupOptions.Lookback"/> (two days) and either has no
/// contact, or has a contact with no name or no address (a bare number
/// flagged by the supervisor, a customer saved in a hurry). Newest callers
/// first, at most
/// <see cref="PosLookupOptions.MaxPerRun"/> a run. Extensions and foreign
/// numbers are skipped; the POS only knows local numbers.
/// </para>
/// <para>
/// <b>Every run asks again.</b> A number the POS did not know is asked about
/// at the next run too, until it has a contact or its calls are two days old:
/// the order is often typed into the POS during or after the call, so the
/// first answer is often "not found" and a later one is not. Until 1 Oct 2026 such a number
/// waited an hour between asks; Dia asked for every run.
/// </para>
/// <para>
/// <b>The contact wins.</b> Whatever an agent typed was typed by somebody who
/// spoke to the customer, so the POS only fills a field that is empty. It
/// never overwrites a name, an address or notes, and it never touches the VIP
/// or Blocked flags, which are the supervisor's alone (S-45). A POS
/// "blacklisted" customer is logged, not blocked.
/// </para>
/// <para>
/// <b>The calls follow.</b> <see cref="ContactCallLinker"/> attaches the
/// number's unmatched calls, with the same rule as when an agent saves the
/// contact, so the customer's history and the reports see them.
/// </para>
/// <para>
/// <b>A POS that cannot be asked stops the run.</b> A refused token or a site
/// that is down would fail for every number alike, so the run ends at the
/// first failure and the numbers it had not reached wait for the next one.
/// A failed request is never taken to mean "not a customer".
/// </para>
/// <para>
/// <b>One run at a time.</b> The timer and the Check now button share
/// <see cref="PosLookupLedger.Gate"/>, so the same number is never asked about
/// twice at once.
/// </para>
/// </remarks>
public class PosCustomerSync(
    CallCenterDbContext db,
    IPosCustomerLookup pos,
    PosLookupLedger ledger,
    ContactCallLinker calls,
    Settings.SettingsService settings,
    IOptions<PosLookupOptions> options,
    TimeProvider clock,
    ILogger<PosCustomerSync> logger)
{
    /// <summary>
    /// How often a run happens, in minutes: the supervisor's setting (S-47),
    /// read on every tick so a change needs no restart.
    /// </summary>
    public const string IntervalMinutesKey = "pos.lookup.interval_minutes";
    public const int DefaultIntervalMinutes = 5;

    /// <summary>What one run did, for the log, the settings screen and the tests.</summary>
    public readonly record struct Result(int Asked, int NotFound, int Created, int FilledIn, int CallsLinked, bool Failed);

    private enum Outcome { Created, FilledIn, Unchanged }

    /// <summary>
    /// Runs when <see cref="IntervalMinutesKey"/> has passed since the last
    /// run began; otherwise does nothing and returns null. The worker asks
    /// every half minute.
    /// </summary>
    /// <remarks>
    /// A run that failed counts as a run, so a POS that is down is asked
    /// once an interval, not every half minute. A run already going (the
    /// Check now button) is left to finish, and this one is skipped.
    /// </remarks>
    public async Task<Result?> RunIfDueAsync(CancellationToken ct = default)
    {
        var minutes = await IntervalMinutesAsync(ct);
        var now = clock.GetUtcNow();

        // A few seconds' grace, so a tick landing a moment early does not
        // skip a whole interval.
        if (ledger.LastRunAt is { } last && now - last < TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(10))
        {
            return null;
        }

        if (!await ledger.Gate.WaitAsync(0, ct))
        {
            return null;
        }

        try
        {
            ledger.LastRunAt = now;
            return await RunOnceAsync(ct);
        }
        finally
        {
            ledger.Gate.Release();
        }
    }

    /// <summary>
    /// The Check now button: runs straight away, after a run already going has
    /// finished. The timer then counts its interval from this run.
    /// </summary>
    public async Task<Result> RunNowAsync(CancellationToken ct = default)
    {
        await ledger.Gate.WaitAsync(ct);

        try
        {
            ledger.LastRunAt = clock.GetUtcNow();
            return await RunOnceAsync(ct);
        }
        finally
        {
            ledger.Gate.Release();
        }
    }

    /// <summary>Whether the lookup is on, and what the last run did, for the settings screen.</summary>
    public async Task<PosLookupStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var last = ledger.Last;

        return new PosLookupStatusDto(
            options.Value.Enabled,
            await IntervalMinutesAsync(ct),
            ledger.Gate.CurrentCount == 0,
            last?.StartedAt,
            last?.FinishedAt,
            last?.Result.Asked ?? 0,
            last?.Result.NotFound ?? 0,
            last?.Result.Created ?? 0,
            last?.Result.FilledIn ?? 0,
            last?.Result.CallsLinked ?? 0,
            last?.Result.Failed ?? false);
    }

    /// <summary>One run, whenever it is called. The tests call it directly; the app goes through the gate.</summary>
    public async Task<Result> RunOnceAsync(CancellationToken ct = default)
    {
        var started = clock.GetUtcNow();
        var result = await AskAsync(started, ct);
        ledger.Last = new PosLookupLedger.Run(started, clock.GetUtcNow(), result);
        return result;
    }

    private async Task<int> IntervalMinutesAsync(CancellationToken ct)
    {
        var minutes = await settings.GetIntAsync(IntervalMinutesKey, DefaultIntervalMinutes, ct);

        // The settings screen allows 1 to 1440, but a row edited by hand to
        // zero would otherwise ask the POS every half minute.
        return minutes < 1 ? DefaultIntervalMinutes : minutes;
    }

    private async Task<Result> AskAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now - options.Value.Lookback;

        var recent = await db.Communications
            .AsNoTracking()
            .Where(c => c.Kind == CommunicationKinds.Call && c.StartedAt >= since && c.RemoteNormalised != null)
            .Where(c => c.ContactId == null
                        || (c.Contact!.DeletedAt == null
                            && c.Contact.MergedIntoId == null
                            && (c.Contact.Name == null || c.Contact.Address == null)))
            .GroupBy(c => c.RemoteNormalised!)
            .Select(g => new { Number = g.Key, Last = g.Max(c => c.StartedAt) })
            .ToListAsync(ct);

        var due = recent
            .Where(r => PhoneNormalizer.ToNational(r.Number) is not null)
            .OrderByDescending(r => r.Last)
            .Take(options.Value.MaxPerRun)
            .Select(r => r.Number)
            .ToList();

        int asked = 0, notFound = 0, created = 0, filledIn = 0, linked = 0;

        foreach (var number in due)
        {
            PosCustomer? customer;

            try
            {
                customer = await pos.FindAsync(number, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "The POS customer lookup failed, so this run stopped after {Asked} of {Due} number(s). "
                    + "The rest are asked at the next run.", asked, due.Count);
                return new Result(asked, notFound, created, filledIn, linked, Failed: true);
            }

            asked++;

            if (customer is null)
            {
                notFound++;
                continue;
            }

            try
            {
                var (outcome, callsLinked) = await ApplyAsync(number, customer, ct);
                linked += callsLinked;
                created += outcome == Outcome.Created ? 1 : 0;
                filledIn += outcome == Outcome.FilledIn ? 1 : 0;
            }
            catch (DbUpdateException ex)
            {
                // Almost always an agent saving the same number a moment ago:
                // theirs stands, and this one is dropped.
                logger.LogWarning(ex, "A contact from the POS could not be saved (POS customer {PosId})", customer.Id);
                db.ChangeTracker.Clear();
            }
        }

        if (asked > 0)
        {
            logger.LogInformation(
                "POS lookup: asked about {Asked} number(s), {NotFound} not known, {Created} contact(s) created, "
                + "{FilledIn} filled in, {Linked} call(s) attached",
                asked, notFound, created, filledIn, linked);
        }

        return new Result(asked, notFound, created, filledIn, linked, Failed: false);
    }

    private async Task<(Outcome Outcome, int CallsLinked)> ApplyAsync(
        string number, PosCustomer customer, CancellationToken ct)
    {
        var name = Trimmed(customer.Name);
        var address = AddressOf(customer);
        var notes = Trimmed(customer.Notes);

        if (customer.Blacklisted)
        {
            logger.LogInformation(
                "The POS marks customer {PosId} as blacklisted. The contact is not blocked: that is the supervisor's decision (S-45).",
                customer.Id);
        }

        var contact = await FindContactAsync(number, ct);
        Outcome outcome;

        if (contact is null)
        {
            // A POS record with nothing in it would make a contact that says
            // no more than the call log already does.
            if (name is null && address is null)
            {
                return (Outcome.Unchanged, 0);
            }

            contact = new Contact
            {
                Name = name,
                NameNormalised = ContactsService.NormalisedName(name),
                Address = address,
                Notes = notes,
                // R-16: new here, although nobody typed it in (F-10).
                Source = ContactSources.Pos,
            };

            contact.Phones.Add(new ContactPhone
            {
                Raw = PhoneNormalizer.ToNational(number)!,
                Normalised = number,
                IsPrimary = true,
            });

            // The POS's second number too, unless it is already somebody's (A-63).
            var second = PhoneNormalizer.Normalize(customer.Phone2);
            if (!PhoneNormalizer.IsExtension(second) && second.Length > 0 && second != number
                && !await db.ContactPhones.AnyAsync(p => p.Normalised == second, ct))
            {
                contact.Phones.Add(new ContactPhone { Raw = customer.Phone2!.Trim(), Normalised = second });
            }

            db.Contacts.Add(contact);
            await db.SaveChangesAsync(ct);
            await AuditAsync("pos-create", contact, before: null, ct);

            logger.LogInformation("Contact {ContactId} created from POS customer {PosId}", contact.Id, customer.Id);
            outcome = Outcome.Created;
        }
        else
        {
            var before = ContactsService.Snapshot(contact);
            var changed = false;

            if (contact.Name is null && name is not null)
            {
                contact.Name = name;
                contact.NameNormalised = ContactsService.NormalisedName(name);
                changed = true;
            }

            if (contact.Address is null && address is not null)
            {
                contact.Address = address;
                changed = true;
            }

            if (contact.Notes is null && notes is not null)
            {
                contact.Notes = notes;
                changed = true;
            }

            if (changed)
            {
                contact.UpdatedBy = null;
                contact.UpdatedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                await AuditAsync("pos-fill", contact, before, ct);

                logger.LogInformation("Contact {ContactId} filled in from POS customer {PosId}", contact.Id, customer.Id);
            }

            outcome = changed ? Outcome.FilledIn : Outcome.Unchanged;
        }

        var callsLinked = await calls.LinkUnmatchedCallsAsync(
            contact.Id, contact.Phones.Select(p => p.Normalised), ct);

        return (outcome, callsLinked);
    }

    /// <summary>The caller lookup's rule (A-13): exact number, else the last nine digits.</summary>
    private async Task<Contact?> FindContactAsync(string number, CancellationToken ct)
    {
        var active = db.Contacts
            .Include(c => c.Phones)
            .Where(c => c.DeletedAt == null && c.MergedIntoId == null);

        var contact = await active.FirstOrDefaultAsync(c => c.Phones.Any(p => p.Normalised == number), ct);

        var last9 = PhoneNormalizer.Last9(number);
        if (contact is null && last9.Length == 9)
        {
            contact = await active.ByLast9(last9).FirstOrDefaultAsync(ct); // M-S11: the same contact every time
        }

        return contact;
    }

    /// <summary>
    /// The street and the city as one line, city first, so the town is the
    /// first thing an agent reads: "رام الله - ترست للتأمين". The city alone is
    /// still worth having, and is not repeated when the address already names it.
    /// </summary>
    private static string? AddressOf(PosCustomer customer)
    {
        var street = Trimmed(customer.Address);
        var city = Trimmed(customer.City);

        if (city is null || (street is not null && street.Contains(city, StringComparison.Ordinal)))
        {
            return street;
        }

        return street is null ? city : $"{city} - {street}";
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>No user: the audit trail shows the POS did it, not an agent (N-06).</summary>
    private async Task AuditAsync(string action, Contact contact, System.Text.Json.JsonDocument? before, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = null,
            Entity = "contact",
            EntityId = contact.Id.ToString(),
            Action = action,
            Before = before,
            After = ContactsService.Snapshot(contact),
        });

        await db.SaveChangesAsync(ct);
    }
}
