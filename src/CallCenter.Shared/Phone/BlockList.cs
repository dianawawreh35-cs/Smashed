namespace CallCenter.Shared.Phone;

/// <summary>
/// The blocked numbers, ready to be asked about a caller (A-17).
/// </summary>
/// <remarks>
/// Built once from the list the server hands out and then asked a yes/no
/// question per incoming call, which is why the matching is prepared up front
/// rather than computed per call: the answer decides whether the agent's phone
/// rings at all, and it is needed before the first ring.
///
/// Matching mirrors caller lookup (A-13): the normalised form first, then the
/// last nine digits, so a caller presenting <c>0599123456</c> is recognised as
/// the number blocked as <c>+970599123456</c>. Without the fallback a blocked
/// nuisance caller would get through simply by the PBX presenting their number
/// in a different format, which is exactly the case A-13 exists for.
///
/// This lives in Shared rather than in the Agent App so that the rule can be
/// tested without a laptop, a PBX or a call.
/// </remarks>
public sealed class BlockList
{
    /// <summary>A block list holding nothing — what an app has before its first refresh.</summary>
    public static readonly BlockList Empty = new([]);

    private readonly HashSet<string> _normalised;
    private readonly HashSet<string> _last9;

    /// <param name="normalisedNumbers">
    /// Digits only, E.164 without the '+', as
    /// <see cref="PhoneNormalizer.Normalize"/> produces and the server stores.
    /// Anything that does not normalise is dropped rather than trusted.
    /// </param>
    public BlockList(IEnumerable<string> normalisedNumbers)
    {
        _normalised = [];
        _last9 = [];

        foreach (var number in normalisedNumbers)
        {
            var normalised = PhoneNormalizer.Normalize(number);
            if (string.IsNullOrEmpty(normalised))
            {
                continue;
            }

            _normalised.Add(normalised);

            // Short numbers have no meaningful tail. Indexing an extension by
            // its last nine digits would block every number ending the same
            // way, and blocking a branch by accident is expensive.
            var last9 = PhoneNormalizer.Last9(normalised);
            if (last9.Length == 9)
            {
                _last9.Add(last9);
            }
        }
    }

    /// <summary>How many numbers are on the list.</summary>
    public int Count => _normalised.Count;

    /// <summary>
    /// Whether a call from this number must be rejected without ringing (A-17).
    /// </summary>
    /// <remarks>
    /// A caller id that is blank, withheld or unrecognisable is <b>not</b>
    /// blocked. Rejecting what cannot be identified would silently drop
    /// withheld-number calls, and A-17 is about specific numbers a supervisor
    /// has named — not about everyone the PBX fails to describe.
    /// </remarks>
    public bool IsBlocked(string? callerNumber)
    {
        var normalised = PhoneNormalizer.Normalize(callerNumber);
        if (string.IsNullOrEmpty(normalised))
        {
            return false;
        }

        if (_normalised.Contains(normalised))
        {
            return true;
        }

        var last9 = PhoneNormalizer.Last9(normalised);
        return last9.Length == 9 && _last9.Contains(last9);
    }
}
