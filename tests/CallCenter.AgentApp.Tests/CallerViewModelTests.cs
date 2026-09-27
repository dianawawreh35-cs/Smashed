using System.Net;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Localization;
using CallCenter.AgentApp.ViewModels;
using CallCenter.Shared.Contracts.Contacts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// Saving a caller as a new customer from the pop-up (A-11).
/// </summary>
public class CallerViewModelTests
{
    private const string Number = "0599123456";

    private static readonly ContactDto Saved = new(
        Guid.NewGuid(), "Test customer", null, null, null, IsVip: false, IsBlocked: false, FlagReason: null,
        [new ContactPhoneDto(Guid.NewGuid(), Number, "+970599123456", IsPrimary: true)],
        CreatedByDisplayName: null, DateTimeOffset.Now, DateTimeOffset.Now);

    private static StubServer Server() => new(request => request.RequestUri!.AbsolutePath switch
    {
        "/api/contacts/by-phone" => StubServer.Problem(HttpStatusCode.NotFound, "contact_not_found"),
        "/api/contacts" => StubServer.Json(HttpStatusCode.Created, Saved),
        var path when path.EndsWith("/card") =>
            StubServer.Json(HttpStatusCode.OK, new CallerCardDto(3, 1, 0, [])),
        _ => StubServer.Status(HttpStatusCode.InternalServerError),
    });

    private static CallerViewModel Caller(StubServer server, System.Windows.Threading.Dispatcher dispatcher) =>
        new(server.Client(new AgentSession()),
            new Localizer(new AgentSettingsStore(NullLogger<AgentSettingsStore>.Instance), NullLogger<Localizer>.Instance),
            dispatcher,
            NullLogger<CallerViewModel>.Instance);

    /// <summary>
    /// F-01. The caller has rung off, so saving the form is what closes the
    /// pop-up, and closing it clears the view model mid-save. That disposed
    /// the lookup's token source, the next line read its token, and the
    /// ObjectDisposedException closed the app.
    /// </summary>
    [Fact]
    public void Saving_after_the_caller_rang_off_closes_the_pop_up_without_crashing() =>
        UiThread.Run(async dispatcher =>
        {
            var server = Server();
            var caller = Caller(server, dispatcher);

            caller.Begin(Number);
            await UiThread.Until(() => caller.State is CallerViewModel.Lookup.NewCustomer);

            caller.OpenNewCustomerCommand.Execute(null);
            caller.FormName = "Test customer";

            // What CallViewModel does once the call is over and nothing else
            // holds the pop-up.
            caller.FormFinished += (_, _) => caller.Clear();

            await caller.SaveNewCustomerCommand.ExecuteAsync(null);

            caller.State.Should().Be(CallerViewModel.Lookup.None, "the pop-up has gone and taken the caller with it");
            server.Requests.Should().NotContain(r => r.EndsWith("/card"),
                "nobody is left to show the totals to");
        });

    /// <summary>
    /// The call is still up, so the pop-up stays and the new customer's totals
    /// load underneath it, as for a caller found by the lookup.
    /// </summary>
    [Fact]
    public void Saving_during_the_call_shows_the_new_customer_and_their_totals() =>
        UiThread.Run(async dispatcher =>
        {
            var server = Server();
            var caller = Caller(server, dispatcher);

            caller.Begin(Number);
            await UiThread.Until(() => caller.State is CallerViewModel.Lookup.NewCustomer);

            caller.OpenNewCustomerCommand.Execute(null);
            caller.FormName = "Test customer";

            await caller.SaveNewCustomerCommand.ExecuteAsync(null);

            caller.State.Should().Be(CallerViewModel.Lookup.Found);
            caller.Name.Should().Be("Test customer");
            caller.Orders.Should().Be(3);
        });
}
