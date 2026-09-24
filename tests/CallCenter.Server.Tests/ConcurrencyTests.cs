using System.Diagnostics;
using System.Net;
using CallCenter.Shared;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// A burst of signed-in requests, all at once, against a real database.
/// </summary>
/// <remarks>
/// On 21 September, 39 signed-in requests arriving together on the development
/// machine answered 7 in 25 seconds, while one at a time took 10 ms each. It is
/// what a busy shift looks like: several agents, each app loading a screen's
/// worth of data at once. By 24 September it no longer reproduced (see
/// DECISIONS.md), and this is the test that would say so if it came back.
///
/// Each request goes through the full path: token validation, the account check
/// that runs on every signed-in request (N-05), and a query. The time limit is
/// generous on purpose, to keep a slow CI machine green. The collapse it guards
/// against was not "a bit slow"; it was requests failing after tens of seconds.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task Fifty_signed_in_requests_at_once_all_succeed_promptly()
    {
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        // Warm up, so the timing is the burst and not the first query's plan.
        (await client.GetAsync("/api/menu")).StatusCode.Should().Be(HttpStatusCode.OK);

        var clock = Stopwatch.StartNew();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => client.GetAsync("/api/menu")));
        clock.Stop();

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            "50 small reads at once took about half a second on the development machine");
    }
}
