using CallCenter.Server.Data;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Contacts;

/// <summary>
/// Gives calls that came from a number, and that nobody could put a name to,
/// the contact that number now belongs to (A-11, A-13, A-62).
/// </summary>
/// <remarks>
/// <para>
/// A call is matched to a contact when it is reported (A-13). A call from a
/// number nobody had on file is stored with no contact, and it stayed that way
/// even after the agent saved the caller as a new customer, sometimes while
/// they were still talking. So the customer's history was missing the very call
/// that made them a customer.
/// </para>
/// <para>
/// This runs whenever numbers are put on a contact: a new contact, a number
/// added, an edit, a bare number flagged. It attaches every earlier call from
/// those numbers <b>that has no contact yet</b>. A call already on a contact is
/// never moved: that was somebody's match, and moving it here would quietly
/// rewrite a customer's history. The order no longer matters either. A call
/// reported before the save is attached here, and one reported after is matched
/// when it arrives.
/// </para>
/// <para>
/// The matching rule is the caller lookup's, not a new one: the exact
/// normalised number, else the last nine digits, and never the last nine for a
/// short number, where every internal extension would collide.
/// </para>
/// <para>
/// <b>The POS lookup will use this.</b> A planned job will ask the restaurant's
/// POS about recent callers nobody has on file, and create or fill in their
/// contacts. Those contacts then need their calls, and this is how they get
/// them (DECISIONS, 24 Sep).
/// </para>
/// </remarks>
public class ContactCallLinker(CallCenterDbContext db, ILogger<ContactCallLinker> logger)
{
    /// <summary>
    /// Attaches to <paramref name="contactId"/> every call with no contact from
    /// any of <paramref name="normalisedNumbers"/>. Returns how many.
    /// </summary>
    public async Task<int> LinkUnmatchedCallsAsync(
        Guid contactId, IEnumerable<string> normalisedNumbers, CancellationToken ct = default)
    {
        var linked = 0;

        foreach (var number in normalisedNumbers.Where(n => !string.IsNullOrEmpty(n)).Distinct())
        {
            var last9 = PhoneNormalizer.Last9(number);
            var tail = last9.Length == 9 ? last9 : null;

            linked += await db.Communications
                .Where(c => c.ContactId == null && c.RemoteNormalised != null)
                .Where(c => c.RemoteNormalised == number
                            || (tail != null && c.RemoteNormalised.EndsWith(tail)))
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.ContactId, contactId), ct);
        }

        if (linked > 0)
        {
            logger.LogInformation("{Count} earlier call(s) attached to contact {ContactId}", linked, contactId);
        }

        return linked;
    }
}
