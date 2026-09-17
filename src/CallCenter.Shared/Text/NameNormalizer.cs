using System.Globalization;
using System.Text;

namespace CallCenter.Shared.Text;

/// <summary>
/// Reduces a person's name to a canonical form for comparison, the way
/// <see cref="Phone.PhoneNormalizer"/> does for numbers.
/// </summary>
/// <remarks>
/// The same Arabic name is written several ways, and PostgreSQL compares none of
/// them as equal — <c>'أحمد' = 'احمد'</c> is false, and so is the <c>ILIKE</c>
/// form. Without this, a duplicate-name warning would almost never fire for the
/// names most customers actually have.
///
/// What is folded, and why:
/// <list type="bullet">
///   <item><b>Diacritics (tashkeel)</b> — optional in writing, so
///     <c>أَحْمَد</c> and <c>أحمد</c> are the same name.</item>
///   <item><b>Tatweel</b> (<c>ـ</c>) — a decorative stretch with no meaning.</item>
///   <item><b>Alef forms</b> <c>أ إ آ ٱ</c> to <c>ا</c> — the hamza is routinely
///     left off when typing.</item>
///   <item><b>Alef maqsura</b> <c>ى</c> to <c>ي</c>, and <b>ta marbuta</b>
///     <c>ة</c> to <c>ه</c> — both are written either way at the end of a
///     name.</item>
///   <item><b>Hamza carriers</b> <c>ؤ</c> to <c>و</c>, <c>ئ</c> to <c>ي</c>.</item>
///   <item><b>Case and spacing</b> — for Latin names, and for the double spaces
///     that come with fast typing.</item>
/// </list>
///
/// This is for <i>matching only</i>. The name as typed is what is stored and
/// shown; this form is never displayed, because it is not how anyone spells
/// their name.
/// </remarks>
public static class NameNormalizer
{
    /// <summary>Arabic diacritics: fathatan through sukun, plus superscript alef.</summary>
    private const string Tashkeel = "ًٌٍَُِّْٰ";

    /// <summary>Tatweel — the decorative character that stretches a word.</summary>
    private const char Tatweel = 'ـ';

    /// <summary>
    /// The canonical form of <paramref name="name"/>, or an empty string when
    /// there is nothing to compare.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var lastWasSpace = false;

        foreach (var ch in name.Trim())
        {
            if (Tashkeel.Contains(ch) || ch == Tatweel)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                // Runs of spaces collapse to one, so "Ahmad  Ali" matches
                // "Ahmad Ali".
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            lastWasSpace = false;
            builder.Append(Fold(ch));
        }

        // Trailing space from a name that ended in whitespace inside the loop.
        return builder.ToString().TrimEnd().ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <see langword="true"/> when two names are the same once normalised — what
    /// the duplicate-name warning asks (A-63).
    /// </summary>
    public static bool AreSame(string? left, string? right)
    {
        var a = Normalize(left);
        return a.Length > 0 && a == Normalize(right);
    }

    private static char Fold(char ch) => ch switch
    {
        // Alef with hamza above, below, madda, and the wasla form.
        'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',

        // Alef maqsura to ya.
        'ى' => 'ي',

        // Ta marbuta to ha.
        'ة' => 'ه',

        // Hamza on waw, and on ya.
        'ؤ' => 'و',
        'ئ' => 'ي',

        _ => ch,
    };
}
