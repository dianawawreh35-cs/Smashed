using CallCenter.Server.Data.Entities;

namespace CallCenter.Server.Features.Contacts;

/// <summary>
/// The fallback of caller matching (A-13): contacts with a number ending in the
/// same nine digits, best first.
/// </summary>
/// <remarks>
/// An exact normalised number belongs to one contact at most (the unique index
/// on <c>contact_phones.normalised</c>), but the last nine digits can be shared:
/// <c>+970 59…</c> and <c>+972 59…</c> are two numbers with one tail. The
/// query used to take whichever row PostgreSQL returned first, so the same
/// caller could land on one customer today and the other tomorrow (M-S11, 27 Sep
/// review). Now it is always the same one: a contact whose <b>primary</b> number
/// matches, then the <b>oldest</b>, then the id, so a tie is still settled.
/// Used by the call log, the pop-up's lookup and the POS sync alike.
/// </remarks>
public static class ContactMatching
{
    public static IQueryable<Contact> ByLast9(this IQueryable<Contact> contacts, string last9) =>
        contacts
            .Where(c => c.Phones.Any(p => p.Last9 == last9))
            .OrderByDescending(c => c.Phones.Any(p => p.Last9 == last9 && p.IsPrimary))
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id);
}
