using System.IO.Compression;
using System.Text;

namespace CallCenter.Server.Data.Seed;

/// <summary>
/// The customer book carried over from the old ordering system: 15,358 contacts
/// exported from it and shipped inside the server assembly, so a new
/// installation starts with the customers the shop already knows (A-61, A-13).
/// </summary>
/// <remarks>
/// The file is <c>Contacts/contacts.csv.gz</c>, an embedded resource — the same
/// choice as the menu photographs, and for the same reason: installing is the
/// one DLL and nothing beside it. Gzipped it is 393 KB, against 3.2 MB of CSV.
///
/// It is produced from <c>docs/Contacts.xlsx</c> by
/// <c>tools/contacts-import/convert.py</c>, which is where the column mapping
/// and the address cleaning are explained. Nothing here parses Excel: the
/// conversion happens once, by hand, and its result is committed.
///
/// The numbers are exactly as they were exported. <see cref="DatabaseSeeder"/>
/// normalises them with
/// <c>CallCenter.Shared.Phone.PhoneNormalizer</c> at install time, so a seeded
/// number and a number an agent types go through the same rules — which is what
/// makes caller matching find these contacts at all.
/// </remarks>
public static class ContactSeedData
{
    /// <summary>One customer, as the export had them.</summary>
    /// <param name="Name">Never blank — a row without one is dropped by the converter.</param>
    /// <param name="Phone">The main number, unnormalised. May be blank.</param>
    /// <param name="Phone2">A second number, unnormalised. Usually blank.</param>
    /// <param name="Address">Street address with the city appended. May be blank.</param>
    /// <param name="Notes">The shop's free-text note, e.g. "واتساب". May be blank.</param>
    public record Row(string Name, string Phone, string Phone2, string Address, string Notes);

    private const string ResourceName = "CallCenter.Server.Data.Seed.Contacts.contacts.csv.gz";

    /// <summary>The header the file must start with, so a wrong file is noticed.</summary>
    private static readonly string[] Columns = ["name", "phone", "phone2", "address", "notes"];

    /// <summary>
    /// Reads the file, one contact at a time. Streamed rather than returned as a
    /// list: 15,000 rows do not need to be in memory at once, and the seeder
    /// inserts them in batches anyway.
    /// </summary>
    /// <returns>An empty sequence when the resource is missing from the assembly.</returns>
    public static IEnumerable<Row> Read()
    {
        var assembly = typeof(ContactSeedData).Assembly;

        using var compressed = assembly.GetManifestResourceStream(ResourceName);

        if (compressed is null)
        {
            yield break;
        }

        using var stream = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var header = ReadRecord(reader);

        if (header is null || !header.SequenceEqual(Columns))
        {
            throw new InvalidDataException(
                $"{ResourceName} does not start with the expected header "
                + $"'{string.Join(',', Columns)}'. Regenerate it with tools/contacts-import/convert.py.");
        }

        while (ReadRecord(reader) is { } fields)
        {
            if (fields.Count < Columns.Length)
            {
                continue;
            }

            yield return new Row(fields[0], fields[1], fields[2], fields[3], fields[4]);
        }
    }

    /// <summary>
    /// Reads one CSV record, or <see langword="null"/> at end of file.
    /// </summary>
    /// <remarks>
    /// RFC 4180: fields separated by commas, a field may be wrapped in double
    /// quotes, and a quote inside such a field is doubled. A quoted field may
    /// contain newlines, which is why this reads records rather than lines —
    /// some of these addresses do.
    ///
    /// Hand-written rather than a package: it reads one file, written by one
    /// script in this repository, and adding a dependency to the server for
    /// that would be the larger thing to maintain.
    /// </remarks>
    private static List<string>? ReadRecord(TextReader reader)
    {
        if (reader.Peek() < 0)
        {
            return null;
        }

        var fields = new List<string>(Columns.Length);
        var field = new StringBuilder();
        var quoted = false;

        while (true)
        {
            var next = reader.Read();

            if (next < 0)
            {
                fields.Add(field.ToString());
                return fields;
            }

            var ch = (char)next;

            if (quoted)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (reader.Peek() == '"')
                {
                    // A doubled quote inside a quoted field is one quote.
                    reader.Read();
                    field.Append('"');
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    return fields;

                default:
                    field.Append(ch);
                    break;
            }
        }
    }
}
