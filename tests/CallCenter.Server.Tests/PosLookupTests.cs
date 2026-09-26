using System.Net;
using System.Net.Http.Json;
using System.Text;
using CallCenter.Server.Data;
using CallCenter.Server.Features.Contacts;
using CallCenter.Server.Features.Pos;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The POS customer lookup (A-67): recent callers nobody has on file get a
/// contact from the POS, and the contact an agent typed always wins.
/// </summary>
/// <remarks>
/// The POS is a fake here, answering for the numbers each test gives it and
/// "not found" for every other recent caller in the database. The real one is
/// never asked: the test host has no token, so its worker does not run.
/// </remarks>
[Collection(ApiCollection.Name)]
public class PosLookupTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private const string Street = "ترست للتأمين - سطح مرحبا";
    private const string City = "رام الله";

    [DatabaseFact]
    public async Task An_unknown_caller_the_POS_knows_becomes_a_contact_with_their_calls()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();
        var second = TestData.NewMobile();
        var earlier = await LogAsync(agent, $"+970{mobile[1..]}", DateTimeOffset.UtcNow.AddHours(-5));
        var latest = await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddMinutes(-2));
        var pos = new FakePos { [mobile] = Customer("ضياء نواورة", mobile, phone2: second, notes: "ويلز نقدي  ") };

        var result = await RunAsync(pos);

        result.Created.Should().BeGreaterThanOrEqualTo(1);
        var contact = await ContactWithAsync(mobile);
        contact.Name.Should().Be("ضياء نواورة");
        contact.Address.Should().Be($"{City} - {Street}");
        contact.Notes.Should().Be("ويلز نقدي");
        contact.CreatedBy.Should().BeNull("the POS made it, not an agent");
        contact.Phones.Select(p => p.Raw).Should().BeEquivalentTo([mobile, second]);
        contact.Phones.Single(p => p.IsPrimary).Raw.Should().Be(mobile);
        (await ContactOfAsync(earlier.Id)).Should().Be(contact.Id);
        (await ContactOfAsync(latest.Id)).Should().Be(contact.Id);
    }

    [DatabaseFact]
    public async Task An_existing_contact_is_only_filled_in_never_overwritten_or_blocked()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();
        var saved = await data.CreateContactAsync("Typed by an agent", mobile);
        await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddMinutes(-10));
        var pos = new FakePos { [mobile] = Customer("POS spelling", mobile, blacklisted: true) };

        await RunAsync(pos);

        var contact = await ContactWithAsync(mobile);
        contact.Id.Should().Be(saved.Id);
        contact.Name.Should().Be("Typed by an agent", "the contact wins over the POS");
        contact.Address.Should().Be($"{City} - {Street}", "an empty field is filled in");
        contact.IsBlocked.Should().BeFalse("blocking is the supervisor's decision (S-45)");
    }

    [DatabaseFact]
    public async Task A_caller_the_POS_does_not_know_is_not_asked_about_again_straight_away()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();
        await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddMinutes(-1));
        var pos = new FakePos();
        var ledger = new PosLookupLedger();

        await RunAsync(pos, ledger);
        await RunAsync(pos, ledger);

        pos.Asked.Count(n => n == PhoneNormalizer.Normalize(mobile)).Should().Be(1);
    }

    [DatabaseFact]
    public async Task A_complete_contact_is_not_asked_about()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();
        await CreateWithAddressAsync(agent, mobile);
        await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddMinutes(-1));
        var pos = new FakePos();

        await RunAsync(pos);

        pos.Asked.Should().NotContain(PhoneNormalizer.Normalize(mobile));
    }

    [DatabaseFact]
    public async Task A_POS_that_cannot_be_asked_stops_the_run_and_the_number_waits_for_the_next()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var mobile = TestData.NewMobile();
        await LogAsync(agent, mobile, DateTimeOffset.UtcNow.AddMinutes(-1));
        var pos = new FakePos { Down = true };
        var ledger = new PosLookupLedger();

        var result = await RunAsync(pos, ledger);

        result.Failed.Should().BeTrue();
        result.Asked.Should().Be(0);
        ledger.IsDue(PhoneNormalizer.Normalize(mobile), DateTimeOffset.UtcNow, TimeSpan.FromHours(1))
            .Should().BeTrue("a failed request is not a \"not found\"");
    }

    [Fact]
    public void A_number_is_asked_about_again_once_the_retry_period_has_passed()
    {
        var ledger = new PosLookupLedger();
        var at = DateTimeOffset.UtcNow;

        ledger.Asked("970599123456", at);

        ledger.IsDue("970599123456", at.AddMinutes(59), TimeSpan.FromHours(1)).Should().BeFalse();
        ledger.IsDue("970599123456", at.AddHours(1), TimeSpan.FromHours(1)).Should().BeTrue();
        ledger.IsDue("970599000000", at, TimeSpan.FromHours(1)).Should().BeTrue();
    }

    [Fact]
    public async Task The_client_sends_the_local_number_with_the_token_and_reads_the_answer()
    {
        // The POS's own answer for a known number, as captured on 26 Sep.
        const string json = """
            [{"CU_ID":8109,"Name":"ضياء نواورة","Phone":"0569498581","Phone2":"","Address1":"ترست للتأمين","City":"رام الله","Notes":"ويلز نقدي  ","CU_BL":false,"LOrder":"2026-09-26T13:45:04","TOrders":0}]
            """;
        var handler = new StubHandler(HttpStatusCode.OK, json);
        var client = Client(handler);

        var customer = await client.FindAsync("970569498581");

        handler.Request!.RequestUri!.ToString().Should().Be("https://pos.test/api/CustLookup/0569498581");
        handler.Request.Headers.Authorization!.ToString().Should().Be("Bearer secret");
        customer.Should().NotBeNull();
        customer!.Id.Should().Be(8109);
        customer.Name.Should().Be("ضياء نواورة");
        customer.City.Should().Be("رام الله");
        customer.Blacklisted.Should().BeFalse();
    }

    [Fact]
    public async Task The_client_reads_404_as_not_a_customer_and_throws_on_anything_else()
    {
        (await Client(new StubHandler(HttpStatusCode.NotFound, "")).FindAsync("970599123456")).Should().BeNull();

        var refused = () => Client(new StubHandler(HttpStatusCode.Unauthorized, "")).FindAsync("970599123456");
        await refused.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task The_client_never_asks_about_an_extension_or_a_foreign_number()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");

        (await Client(handler).FindAsync("2001")).Should().BeNull();
        (await Client(handler).FindAsync("442071234567")).Should().BeNull();
        handler.Request.Should().BeNull();
    }

    [DatabaseFact]
    public async Task A_run_waits_until_the_supervisor_s_interval_has_passed()
    {
        // pos.lookup.interval_minutes is unset in the test database: five minutes.
        var ledger = new PosLookupLedger { LastRunAt = DateTimeOffset.UtcNow.AddMinutes(-2) };

        (await RunAsync(new FakePos(), ledger, ifDue: true)).Should().BeNull("two minutes is not five");

        ledger.LastRunAt = DateTimeOffset.UtcNow.AddMinutes(-6);
        (await RunAsync(new FakePos(), ledger, ifDue: true)).Should().NotBeNull();
        ledger.LastRunAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    private async Task<PosCustomerSync.Result> RunAsync(FakePos pos, PosLookupLedger? ledger = null) =>
        (await RunAsync(pos, ledger, ifDue: false))!.Value;

    private async Task<PosCustomerSync.Result?> RunAsync(FakePos pos, PosLookupLedger? ledger, bool ifDue)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var sync = new PosCustomerSync(
            services.GetRequiredService<CallCenterDbContext>(),
            pos,
            ledger ?? new PosLookupLedger(),
            services.GetRequiredService<ContactCallLinker>(),
            services.GetRequiredService<Features.Settings.SettingsService>(),
            Options.Create(new PosLookupOptions { Token = "test", MaxPerRun = 10_000 }),
            TimeProvider.System,
            NullLogger<PosCustomerSync>.Instance);

        var result = ifDue ? await sync.RunIfDueAsync() : await sync.RunOnceAsync();

        // Contacts have no test pattern, so the sweep is told about the ones made here.
        foreach (var number in pos.Known)
        {
            var id = await data.QueryAsync(db => db.ContactPhones
                .Where(p => p.Normalised == number).Select(p => (Guid?)p.ContactId).FirstOrDefaultAsync());
            if (id is { } contactId)
            {
                TestSweeper.Contacts.Add(contactId);
            }
        }

        return result;
    }

    private static PosCustomer Customer(
        string name, string phone, string phone2 = "", string notes = "", bool blacklisted = false) =>
        new(Random.Shared.Next(1, 100_000), name, phone, phone2, Street, City, notes, blacklisted);

    private static PosCustomerClient Client(StubHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new PosLookupOptions { BaseUrl = "https://pos.test/api/CustLookup/", Token = "secret" }));

    private Task<Data.Entities.Contact> ContactWithAsync(string number)
    {
        var normalised = PhoneNormalizer.Normalize(number);
        return data.QueryAsync(db => db.Contacts.AsNoTracking().Include(c => c.Phones)
            .SingleAsync(c => c.Phones.Any(p => p.Normalised == normalised)));
    }

    private static async Task CreateWithAddressAsync(HttpClient agent, string number)
    {
        var response = await agent.PostAsJsonAsync("/api/contacts",
            new Shared.Contracts.Contacts.UpsertContactRequest("Complete", "Somewhere", null, null, [number]));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    private static async Task<CommunicationDto> LogAsync(HttpClient agent, string number, DateTimeOffset started)
    {
        var response = await agent.PostAsJsonAsync("/api/communications/calls", new LogCallRequest(
            TestData.NewSipCallId(), "9000", Directions.In, CommunicationStatuses.Answered, number,
            RemoteName: null, started, started.AddSeconds(3), started.AddMinutes(1), Queue: null, TestData.LaptopId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CommunicationDto>())!;
    }

    private Task<Guid?> ContactOfAsync(Guid callId) =>
        data.QueryAsync(db => db.Communications.Where(c => c.Id == callId).Select(c => c.ContactId).SingleAsync());

    /// <summary>A POS that knows the numbers it is given, keyed as a customer would type them.</summary>
    private sealed class FakePos : IPosCustomerLookup
    {
        private readonly Dictionary<string, PosCustomer> _customers = [];

        public List<string> Asked { get; } = [];

        public bool Down { get; init; }

        public IEnumerable<string> Known => _customers.Keys;

        public PosCustomer this[string number]
        {
            set => _customers[PhoneNormalizer.Normalize(number)] = value;
        }

        public Task<PosCustomer?> FindAsync(string normalisedNumber, CancellationToken ct = default)
        {
            if (Down)
            {
                throw new HttpRequestException("The POS is down", null, HttpStatusCode.BadGateway);
            }

            Asked.Add(normalisedNumber);
            return Task.FromResult(_customers.GetValueOrDefault(normalisedNumber));
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
