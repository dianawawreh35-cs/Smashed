using CallCenter.Shared.Phone;
using SIPSorcery.SIP;

namespace CallCenter.AgentApp.Services.Sip;

/// <summary>Who is calling, as far as the INVITE says (A-10, A-13).</summary>
/// <param name="Number">The caller's number, or null when it was withheld or absent.</param>
/// <param name="DisplayName">
/// The name the PBX sent, when it is not simply the number again. Shown under the
/// number until the contact lookup replaces it with the customer's real name.
/// </param>
/// <param name="IsAnonymous">The caller withheld their number.</param>
public readonly record struct CallerIdentity(string? Number, string? DisplayName, bool IsAnonymous)
{
    /// <summary>Nothing usable in the INVITE at all.</summary>
    public static CallerIdentity None => new(null, null, false);

    /// <summary>Caller ID withheld.</summary>
    public static CallerIdentity Anonymous => new(null, null, true);
}

/// <summary>
/// Pulls the caller's number out of an INVITE.
/// </summary>
/// <remarks>
/// Not just <c>From</c>. An Asterisk extension-to-extension call puts the number
/// there, but a call arriving over a trunk often carries the real subscriber
/// number in <c>P-Asserted-Identity</c> instead, with <c>From</c> holding
/// something the caller chose. RFC 3325 makes P-Asserted-Identity the identity
/// the network vouches for, so it wins when both look like numbers.
///
/// Taken from the proof-of-concept app that already works against this PBX,
/// with the number rules replaced by <see cref="PhoneNormalizer"/> so there is
/// one definition of "looks like a phone number" in this system rather than two.
/// </remarks>
public static class CallerId
{
    /// <summary>
    /// What a PBX puts in From or P-Asserted-Identity when the caller withheld
    /// their number. There is no standard value, so this is a list of what
    /// switches actually send.
    /// </summary>
    private static readonly string[] AnonymousMarkers =
        ["anonymous", "unknown", "unavailable", "restricted", "private", "withheld"];

    public static CallerIdentity FromInvite(SIPRequest invite)
    {
        var fromUser = invite.Header.From?.FromURI?.User;
        var fromName = invite.Header.From?.FromName;

        var asserted = invite.Header.PassertedIdentity?.FirstOrDefault();
        var assertedUser = asserted?.URI?.User;
        var assertedName = asserted?.Name;

        // Withheld if any of the four says so — but the network can still assert
        // a real number alongside an anonymous From, so this only suppresses the
        // display name rather than abandoning the search.
        var anonymous = IsAnonymousMarker(fromUser) || IsAnonymousMarker(fromName)
                        || IsAnonymousMarker(assertedUser) || IsAnonymousMarker(assertedName);

        // Number first, name second, so the display name always comes from the
        // same header the number did.
        (string? Number, string? Name)[] candidates =
        [
            (assertedUser, assertedName),
            (fromUser, fromName),
            (assertedName, null),
            (fromName, null),
        ];

        foreach (var (number, name) in candidates)
        {
            if (!LooksLikeNumber(number))
            {
                continue;
            }

            var trimmed = number!.Trim();
            return new CallerIdentity(
                trimmed,
                anonymous ? null : DisplayNameFor(trimmed, name),
                anonymous);
        }

        if (anonymous)
        {
            return CallerIdentity.Anonymous;
        }

        // Nothing numeric. Fall back to the user part, so an alphanumeric SIP
        // address is shown rather than nothing at all.
        foreach (var (value, name) in new[] { (fromUser, fromName), (assertedUser, assertedName) })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmed = value.Trim();
                return new CallerIdentity(trimmed, DisplayNameFor(trimmed, name), false);
            }
        }

        return CallerIdentity.None;
    }

    /// <summary>Anything with a digit in it, once the separators are stripped.</summary>
    private static bool LooksLikeNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PhoneNormalizer.DigitsOnly(value).Length > 0;

    /// <summary>A display name is only worth showing when it is not the number again.</summary>
    private static string? DisplayNameFor(string number, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim().Trim('"');

            if (trimmed.Length == 0
                || string.Equals(trimmed, number, StringComparison.OrdinalIgnoreCase)
                || PhoneNormalizer.Normalize(trimmed) == PhoneNormalizer.Normalize(number))
            {
                continue;
            }

            return trimmed;
        }

        return null;
    }

    private static bool IsAnonymousMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().Trim('"');
        return AnonymousMarkers.Any(m => string.Equals(trimmed, m, StringComparison.OrdinalIgnoreCase));
    }
}
