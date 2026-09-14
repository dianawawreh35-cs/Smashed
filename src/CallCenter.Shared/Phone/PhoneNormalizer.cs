using System.Text;

namespace CallCenter.Shared.Phone;

/// <summary>
/// Normalises Palestinian (and neighbouring) phone numbers to a canonical
/// E.164 form <b>without</b> the leading <c>+</c>, which is the form stored in
/// the database and used for contact matching.
/// </summary>
/// <remarks>
/// Rules applied, in order:
/// <list type="bullet">
///   <item>All separators are stripped: spaces, dashes, dots, slashes, parentheses,
///     non-breaking spaces; Arabic-Indic digits are folded to ASCII digits.</item>
///   <item>An international prefix of <c>+</c>, <c>00</c> or <c>011</c> is removed.</item>
///   <item><c>970</c> (Palestine) is kept; a <c>0</c> trunk prefix after it is dropped
///     (<c>970 05x...</c> becomes <c>9705x...</c>).</item>
///   <item><c>972</c> (Israel) is kept exactly as dialled - numbers in the field are
///     shared across both plans and we must not rewrite them.</item>
///   <item>A national mobile <c>05xxxxxxxx</c> becomes <c>9705xxxxxxxx</c>.</item>
///   <item>A national landline <c>0(2|4|8|9)xxxxxxx</c> becomes <c>970(2|4|8|9)xxxxxxx</c>.</item>
///   <item>Subscriber-only numbers (<c>5xxxxxxxx</c>, <c>2xxxxxxx</c>) get <c>970</c> prepended.</item>
///   <item>Short numbers (internal extensions, service codes - 5 digits or fewer) are
///     returned as bare digits and never given a country code.</item>
///   <item>Anything else is returned as bare digits, unchanged.</item>
/// </list>
/// </remarks>
public static class PhoneNormalizer
{
    /// <summary>Country calling code for Palestine.</summary>
    public const string PalestineCountryCode = "970";

    /// <summary>Country calling code for Israel - recognised but never rewritten.</summary>
    public const string IsraelCountryCode = "972";

    /// <summary>Numbers of this length or shorter are treated as extensions / service codes.</summary>
    public const int MaxExtensionLength = 5;

    /// <summary>Palestinian landline area codes, without the national trunk prefix.</summary>
    private static readonly char[] LandlineAreaCodes = { '2', '4', '8', '9' };

    /// <summary>
    /// Strips every non-digit character and folds Arabic-Indic / Eastern-Arabic
    /// digits to ASCII. Returns an empty string for <see langword="null"/> or blank input.
    /// </summary>
    public static string DigitsOnly(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (ch is >= '٠' and <= '٩')
            {
                // Arabic-Indic digits
                builder.Append((char)('0' + (ch - '٠')));
            }
            else if (ch is >= '۰' and <= '۹')
            {
                // Extended Arabic-Indic digits
                builder.Append((char)('0' + (ch - '۰')));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalises <paramref name="input"/> to E.164 without the leading <c>+</c>.
    /// Returns an empty string when the input contains no digits.
    /// </summary>
    public static string Normalize(string? input)
    {
        var hadPlus = input is not null && input.TrimStart().StartsWith('+');
        var digits = DigitsOnly(input);

        if (digits.Length == 0)
        {
            return string.Empty;
        }

        // International access prefixes (only when not already flagged by '+').
        if (!hadPlus)
        {
            if (digits.StartsWith("00", StringComparison.Ordinal) && digits.Length > 2)
            {
                digits = digits[2..];
            }
            else if (digits.StartsWith("011", StringComparison.Ordinal) && digits.Length > 3)
            {
                digits = digits[3..];
            }
        }

        if (digits.Length == 0)
        {
            return string.Empty;
        }

        // Internal extensions and short service codes are never country-coded.
        if (digits.Length <= MaxExtensionLength)
        {
            return digits;
        }

        // Israel - recognised, kept verbatim.
        if (digits.StartsWith(IsraelCountryCode, StringComparison.Ordinal))
        {
            return digits;
        }

        // Palestine with an explicit country code; drop a trunk '0' after it.
        if (digits.StartsWith(PalestineCountryCode, StringComparison.Ordinal))
        {
            var national = digits[PalestineCountryCode.Length..].TrimStart('0');
            return national.Length == 0 ? digits : PalestineCountryCode + national;
        }

        // National form with the trunk prefix: 05xxxxxxxx / 02xxxxxxx.
        if (digits[0] == '0')
        {
            var national = digits.TrimStart('0');
            if (national.Length == 0)
            {
                return digits;
            }

            return IsPalestinianSubscriber(national)
                ? PalestineCountryCode + national
                : national;
        }

        // Subscriber-only form: 5xxxxxxxx / 2xxxxxxx.
        if (IsPalestinianSubscriber(digits))
        {
            return PalestineCountryCode + digits;
        }

        return digits;
    }

    /// <summary>
    /// The last nine digits of the normalised number - the fuzzy key used to match
    /// a caller against a contact regardless of how the number was dialled. Shorter
    /// numbers (extensions) are returned whole.
    /// </summary>
    public static string Last9(string? input)
    {
        var normalized = Normalize(input);
        return normalized.Length <= 9 ? normalized : normalized[^9..];
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="input"/> normalises to a
    /// Palestinian number (country code <c>970</c>).
    /// </summary>
    public static bool IsPalestinian(string? input) =>
        Normalize(input).StartsWith(PalestineCountryCode, StringComparison.Ordinal);

    /// <summary>
    /// <see langword="true"/> when <paramref name="input"/> is short enough to be an
    /// internal extension or service code rather than a real subscriber number.
    /// </summary>
    public static bool IsExtension(string? input)
    {
        var digits = DigitsOnly(input);
        return digits.Length is > 0 and <= MaxExtensionLength;
    }

    /// <summary>
    /// Formats a number for display: <c>+970 5x xxx xxxx</c> for mobiles,
    /// <c>+970 x xxx xxxx</c> for landlines, <c>+cc...</c> otherwise, and bare digits
    /// for extensions.
    /// </summary>
    public static string Format(string? input)
    {
        var normalized = Normalize(input);
        if (normalized.Length <= MaxExtensionLength)
        {
            return normalized;
        }

        if (!normalized.StartsWith(PalestineCountryCode, StringComparison.Ordinal))
        {
            return "+" + normalized;
        }

        var national = normalized[PalestineCountryCode.Length..];
        return national.Length switch
        {
            9 => $"+{PalestineCountryCode} {national[..2]} {national[2..5]} {national[5..]}",
            8 => $"+{PalestineCountryCode} {national[..1]} {national[1..4]} {national[4..]}",
            _ => "+" + normalized,
        };
    }

    /// <summary>
    /// A subscriber number is Palestinian when it is a 9-digit mobile starting with
    /// <c>5</c>, or an 8-digit landline starting with area code 2, 4, 8 or 9.
    /// </summary>
    private static bool IsPalestinianSubscriber(string national) => national.Length switch
    {
        9 => national[0] == '5',
        8 => Array.IndexOf(LandlineAreaCodes, national[0]) >= 0,
        _ => false,
    };
}
