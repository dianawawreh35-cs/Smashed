using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Features.Contacts;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// A new customer's earlier calls are attached to them (A-11, A-62), against a
/// real database.
/// </summary>
/// <remarks>
/// The call the agent was on when they saved the caller as a new customer may
/// reach the server first, and was then stored with no contact for good. Now
/// saving a number on a contact attaches its unmatched calls, so the order no
/// longer matters.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ContactCallLinkTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task Saving_a_new_customer_attaches_the_calls_their_number_already_made()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();

        // Two calls reported while nobody had the number: the one before, and
        // the one the agent is on, reported as it was answered, in the PBX's form.
        var earlier = await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddDays(-2));
        var current = await LogAsync(agent, $"+970{mobile[1..]}", DateTimeOffset.UtcNow.AddMinutes(-1));
        earlier.ContactId.Should().BeNull();

        var saved = await CreateAsync(agent, "New customer", mobile);

        (await ContactOfAsync(earlier.Id)).Should().Be(saved.Id);
        (await ContactOfAsync(current.Id)).Should().Be(saved.Id, "the call they were on is the one A-11 names");
    }

    [DatabaseFact]
    public async Task A_call_reported_after_the_save_is_matched_as_it_arrives()
    {
        // The other order, which already worked: shown so both stay true.
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();

        var saved = await CreateAsync(agent, "Saved mid-call", mobile);
        var call = await LogAsync(agent, mobile, DateTimeOffset.UtcNow);

        call.ContactId.Should().Be(saved.Id);
    }

    [DatabaseFact]
    public async Task Adding_a_number_to_an_existing_contact_brings_that_number_s_calls()
    {
        // "Add this number to them", when the same-name warning is the same person.
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var contact = await data.CreateContactAsync("Has two phones", TestData.NewMobile());
        var second = TestData.NewMobile();
        var call = await LogAsync(agent, second, DateTimeOffset.UtcNow.AddHours(-3));

        var response = await agent.PostAsJsonAsync($"/api/contacts/{contact.Id}/phones", new { number = second });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await ContactOfAsync(call.Id)).Should().Be(contact.Id);
    }

    [DatabaseFact]
    public async Task A_call_already_on_a_contact_is_never_moved()
    {
        // Somebody's match stands. Only calls nobody could name are attached.
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();
        var call = await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddDays(-1));
        var elsewhere = await data.CreateContactAsync("Matched before", TestData.NewMobile());
        await data.QueryAsync(db => db.Communications.Where(c => c.Id == call.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ContactId, elsewhere.Id)));

        await CreateAsync(agent, "New owner of the number", mobile);

        (await ContactOfAsync(call.Id)).Should().Be(elsewhere.Id);
    }

    [DatabaseFact]
    public async Task A_short_number_is_matched_exactly_and_never_on_its_tail()
    {
        // An internal extension has no meaningful last nine digits.
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var extension = Random.Shared.Next(20000, 29999).ToString();
        var exact = await LogAsync(agent, extension, DateTimeOffset.UtcNow.AddHours(-1));
        var longer = await LogAsync(agent, $"9{extension}", DateTimeOffset.UtcNow.AddHours(-1));
        var contact = await data.CreateContactAsync("Extension", extension);

        var linked = await data.QueryAsync(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ContactCallLinker>()
                .LinkUnmatchedCallsAsync(contact.Id, [PhoneNormalizer.Normalize(extension)]);
        });

        linked.Should().Be(1);
        (await ContactOfAsync(exact.Id)).Should().Be(contact.Id);
        (await ContactOfAsync(longer.Id)).Should().BeNull();
    }

    private static async Task<CommunicationDto> LogAsync(HttpClient agent, string number, DateTimeOffset started)
    {
        var response = await agent.PostAsJsonAsync("/api/communications/calls", new LogCallRequest(
            TestData.NewSipCallId(), "9000", Directions.In, CommunicationStatuses.Answered, number,
            RemoteName: null, started, started.AddSeconds(3), started.AddMinutes(1), Queue: null, TestData.LaptopId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CommunicationDto>())!;
    }

    private static async Task<ContactDto> CreateAsync(HttpClient agent, string name, string number)
    {
        var response = await agent.PostAsJsonAsync("/api/contacts",
            new UpsertContactRequest(name, null, null, null, [number]));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    private Task<Guid?> ContactOfAsync(Guid callId) =>
        data.QueryAsync(db => db.Communications.Where(c => c.Id == callId).Select(c => c.ContactId).SingleAsync());
}
