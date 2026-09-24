using System.Net;
using System.Net.Http.Json;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The same call reported several times at the same moment (A-14).
/// </summary>
/// <remarks>
/// Seen on a real call on 24 September: the Agent App sent one call twice in
/// the same millisecond, both requests looked for it, neither found it, and the
/// second insert hit the unique index and answered 500. The recording queued
/// behind the call never went up. The app no longer sends twice, but the server
/// promises that reporting a call twice is harmless, and it has to keep that
/// promise when the two arrive together, not only one after the other.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CallReportedTwiceTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task The_same_call_reported_at_once_is_stored_once_and_every_report_succeeds()
    {
        await data.EnsurePhoneChannelAsync();
        var agent = await data.CreateUserAsync();
        var (client, _) = await data.SignInAsync(agent);

        // Several rounds, several at once: the race needs two inserts in the
        // same instant, and one round may not produce it.
        for (var round = 0; round < 5; round++)
        {
            var started = DateTimeOffset.UtcNow.AddMinutes(-2);
            var call = new LogCallRequest(
                TestData.NewSipCallId(),
                agent.Extension!,
                Directions.In,
                CommunicationStatuses.Answered,
                TestData.NewMobile(),
                RemoteName: null,
                started,
                started.AddSeconds(5),
                started.AddMinutes(1),
                Queue: null,
                TestData.LaptopId);

            var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => client.PostAsJsonAsync("/api/communications/calls", call)));

            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(
                    HttpStatusCode.OK,
                    "a repeat of a call is an update, however close together they arrive: "
                    + await response.Content.ReadAsStringAsync());
            }

            var stored = await data.QueryAsync(db => db.Communications
                .CountAsync(c => c.SipCallId == call.SipCallId && c.Extension == call.Extension));

            stored.Should().Be(1, "one call is one row");
        }
    }
}
