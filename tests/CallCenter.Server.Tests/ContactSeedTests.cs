using CallCenter.Server.Data.Seed;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Checks the customer book that ships inside the server assembly (A-61).
/// </summary>
/// <remarks>
/// The seed itself cannot be run here — the suite has no PostgreSQL — so these
/// check the two things that would otherwise only be discovered during an
/// installation: that the file is in the assembly and readable at all, and that
/// what it holds will survive the constraints on <c>contact_phones</c>.
/// </remarks>
public class ContactSeedDataTests
{
    /// <summary>Read once; the file is 15,000 rows and every test wants all of them.</summary>
    private static readonly IReadOnlyList<ContactSeedData.Row> Rows = ContactSeedData.Read().ToList();

    [Fact]
    public void The_export_is_embedded_and_readable()
    {
        // A missing EmbeddedResource in the csproj, or a file regenerated with
        // different columns, shows up here rather than as an empty contacts
        // list on a freshly installed server.
        Rows.Should().HaveCount(15358);
    }

    [Fact]
    public void Every_contact_has_a_name()
    {
        // The schema allows a nameless contact - a number flagged before anyone
        // knows whose it is (S-45) - but nothing in the export is one.
        Rows.Where(r => string.IsNullOrWhiteSpace(r.Name)).Should().BeEmpty();
    }

    [Fact]
    public void Every_contact_has_a_number_that_normalises()
    {
        // A row whose numbers all normalise to nothing would be skipped at
        // install time, silently losing a customer.
        Rows.Where(r =>
                PhoneNormalizer.Normalize(r.Phone).Length == 0
                && PhoneNormalizer.Normalize(r.Phone2).Length == 0)
            .Should().BeEmpty();
    }

    [Fact]
    public void Nearly_every_number_is_Palestinian()
    {
        // Not a rule, a smell test: if a change to the converter mangled the
        // numbers, most of them would stop looking like local ones.
        var numbers = Rows
            .SelectMany(r => new[] { r.Phone, r.Phone2 })
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        numbers.Count(PhoneNormalizer.IsPalestinian)
            .Should().BeGreaterThan((int)(numbers.Count * 0.99));
    }

    [Fact]
    public void The_repeated_numbers_are_the_ones_the_seeder_expects_to_drop()
    {
        // ux_contact_phones_normalised refuses a number twice. The seeder drops
        // the repeat rather than failing the install, and its remark says how
        // many there are - this is what keeps that number honest when a newer
        // export is converted.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        var rowsLeftWithNothing = 0;

        foreach (var row in Rows)
        {
            var kept = 0;

            foreach (var raw in new[] { row.Phone, row.Phone2 })
            {
                var normalised = PhoneNormalizer.Normalize(raw);

                if (normalised.Length == 0)
                {
                    continue;
                }

                if (seen.Add(normalised))
                {
                    kept++;
                }
                else
                {
                    duplicates++;
                }
            }

            if (kept == 0)
            {
                rowsLeftWithNothing++;
            }
        }

        duplicates.Should().Be(110);
        rowsLeftWithNothing.Should().Be(69);
        (Rows.Count - rowsLeftWithNothing).Should().Be(15289, "that is how many contacts a fresh install gets");
    }

    [Fact]
    public void The_city_is_kept_with_the_address()
    {
        // The export had the city in its own column and this schema has no such
        // field, so the converter appends it. Losing it would make a Jerusalem
        // address read as a Ramallah one.
        Rows.Count(r => r.Address.Contains("القدس")).Should().BeGreaterThan(1000);
    }
}
