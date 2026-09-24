using System.Net;
using System.Net.Http.Json;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// VIP and Blocked, once past the door, against a real database (S-45, A-17, N-06).
/// </summary>
/// <remarks>
/// <see cref="ContactFlagsEndpointTests"/> checks who may flag and what is
/// refused before a query. These check what a flag does: which contact it
/// lands on, the nameless contact a bare number becomes, the audit row, and the
/// block list the Agent App caches from it.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ContactFlagsDatabaseTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task Flagging_a_number_a_contact_has_flags_that_contact()
    {
        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("Known customer", mobile);
        var (client, _) = await SupervisorAsync();

        // The PBX's form of the number, not the one the contact was saved with:
        // flagging and caller lookup must reach the same contact (A-13).
        var flagged = await FlagNumberAsync(client, $"+970{mobile[1..]}", isVip: true, reason: "Orders every day");

        flagged.Id.Should().Be(contact.Id);
        flagged.Name.Should().Be("Known customer");
        flagged.IsVip.Should().BeTrue();

        var contactsWithNumber = await data.QueryAsync(db =>
            db.ContactPhones.CountAsync(p => p.Normalised == PhoneNormalizer.Normalize(mobile)));
        contactsWithNumber.Should().Be(1, "no second contact was created for a number already on file");
    }

    [DatabaseFact]
    public async Task Flagging_a_number_nobody_has_creates_a_nameless_contact_to_carry_it()
    {
        var mobile = TestData.NewMobile();
        var (client, _) = await SupervisorAsync();

        var flagged = await FlagNumberAsync(client, mobile, isBlocked: true, reason: "Prank calls");

        flagged.Name.Should().BeNull();
        flagged.IsBlocked.Should().BeTrue();
        flagged.FlagReason.Should().Be("Prank calls");
        flagged.Numbers.Should().Equal(mobile);

        var stored = await data.QueryAsync(db =>
            db.Contacts.Include(c => c.Phones).AsNoTracking().SingleAsync(c => c.Id == flagged.Id));
        stored.Phones.Should().ContainSingle(p => p.Normalised == PhoneNormalizer.Normalize(mobile));
    }

    [DatabaseFact]
    public async Task Every_flag_change_is_written_to_the_audit_log_with_who_and_when()
    {
        var contact = await data.CreateContactAsync("Audited", TestData.NewMobile());
        var supervisor = await data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await data.SignInAsync(supervisor);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        (await client.PutAsJsonAsync($"/api/contacts/{contact.Id}/flags",
            new SetContactFlagsRequest(false, true, "Abusive"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/contacts/{contact.Id}/flags",
            new SetContactFlagsRequest(false, false, null))).StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await data.QueryAsync(db => db.AuditLog.AsNoTracking()
            .Where(e => e.Entity == "contact" && e.EntityId == contact.Id.ToString())
            .OrderBy(e => e.Id)
            .ToListAsync());

        entries.Select(e => e.Action).Should().Equal("flag", "unflag");
        entries.Should().OnlyContain(e => e.UserId == supervisor.Id && e.At >= before);

        // The same history, as the supervisor app reads it.
        var history = await client.GetFromJsonAsync<List<ContactFlagChangeDto>>(
            $"/api/contacts/{contact.Id}/flags/history");

        history!.Should().HaveCount(2);
        history[0].IsBlocked.Should().BeFalse("newest first: the unblock");
        history[1].IsBlocked.Should().BeTrue();
        history[1].Reason.Should().Be("Abusive");
        history.Should().OnlyContain(h => h.ByDisplayName == supervisor.DisplayName);
    }

    [DatabaseFact]
    public async Task Clearing_the_flags_clears_the_reason_too()
    {
        // A stale "abusive on the phone" beside an unflagged contact reads as if
        // they were still blocked.
        var contact = await data.CreateContactAsync("Forgiven", TestData.NewMobile());
        var (client, _) = await SupervisorAsync();

        await client.PutAsJsonAsync($"/api/contacts/{contact.Id}/flags",
            new SetContactFlagsRequest(false, true, "Abusive"));
        var response = await client.PutAsJsonAsync($"/api/contacts/{contact.Id}/flags",
            new SetContactFlagsRequest(false, false, null));

        var cleared = (await response.Content.ReadFromJsonAsync<FlaggedContactDto>())!;
        cleared.IsBlocked.Should().BeFalse();
        cleared.FlagReason.Should().BeNull();
    }

    [DatabaseFact]
    public async Task A_blocked_contact_puts_every_one_of_its_numbers_on_the_block_list()
    {
        // The list the Agent App caches and rejects calls from before they ring
        // (A-17). A contact with two phones is blocked on both.
        var first = TestData.NewMobile();
        var second = TestData.NewMobile();
        var contact = await data.CreateContactAsync("Two phones", first, second);
        var (client, _) = await SupervisorAsync();

        await client.PutAsJsonAsync($"/api/contacts/{contact.Id}/flags",
            new SetContactFlagsRequest(false, true, "Abusive"));

        var (agentClient, _) = await data.SignInAsync(await data.CreateUserAsync());
        var list = await agentClient.GetFromJsonAsync<BlockedNumbersDto>("/api/contacts/blocked-numbers");

        list!.Numbers.Should().Contain([PhoneNormalizer.Normalize(first), PhoneNormalizer.Normalize(second)]);

        // And off it again when unblocked, without anyone signing out.
        await client.PutAsJsonAsync($"/api/contacts/{contact.Id}/flags",
            new SetContactFlagsRequest(false, false, null));
        list = await agentClient.GetFromJsonAsync<BlockedNumbersDto>("/api/contacts/blocked-numbers");

        list!.Numbers.Should().NotContain(PhoneNormalizer.Normalize(first));
        list.Numbers.Should().NotContain(PhoneNormalizer.Normalize(second));
    }

    [DatabaseFact]
    public async Task A_VIP_is_not_on_the_block_list()
    {
        var mobile = TestData.NewMobile();
        var (client, _) = await SupervisorAsync();
        await FlagNumberAsync(client, mobile, isVip: true, reason: "Regular");

        var list = await client.GetFromJsonAsync<BlockedNumbersDto>("/api/contacts/blocked-numbers");

        list!.Numbers.Should().NotContain(PhoneNormalizer.Normalize(mobile));
    }

    [DatabaseFact]
    public async Task Flagging_a_contact_that_does_not_exist_is_404()
    {
        var (client, _) = await SupervisorAsync();

        var response = await client.PutAsJsonAsync($"/api/contacts/{Guid.NewGuid()}/flags",
            new SetContactFlagsRequest(true, false, "Regular"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(HttpClient Client, Shared.Contracts.Auth.LoginResponse Login)> SupervisorAsync() =>
        await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

    private static async Task<FlaggedContactDto> FlagNumberAsync(
        HttpClient client, string number, bool isVip = false, bool isBlocked = false, string? reason = null)
    {
        var response = await client.PostAsJsonAsync("/api/contacts/flags/by-number",
            new FlagNumberRequest(number, isVip, isBlocked, reason));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FlaggedContactDto>())!;
    }
}
