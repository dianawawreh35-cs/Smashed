using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.AgentLogs;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The endpoint the Agent Apps send their logs to (N-12): agents only, and the
/// offset rule through HTTP as the store keeps it.
/// </summary>
[Collection(ApiCollection.Name)]
public class AgentLogsEndpointTests(CallCenterApiFactory factory)
{
    private const string File = "agent-20260927.log";

    private readonly TestData data = new(factory);

    private static string Laptop() => "TEST-" + Guid.NewGuid().ToString("N")[..8];

    private static ByteArrayContent Body(string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    [DatabaseFact]
    public async Task An_agent_sends_pieces_and_a_repeat_is_refused_with_the_length()
    {
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());
        var laptop = Laptop();

        var first = await client.PostAsync($"/api/agent-logs/{laptop}/{File}?offset=0", Body("one\n"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<AgentLogLengthDto>())!.Length.Should().Be(4);

        var repeat = await client.PostAsync($"/api/agent-logs/{laptop}/{File}?offset=0", Body("one\n"));
        repeat.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repeat.Content.ReadFromJsonAsync<AgentLogLengthDto>())!.Length.Should().Be(4);

        var lengths = await client.GetFromJsonAsync<Dictionary<string, long>>($"/api/agent-logs/{laptop}");
        lengths.Should().Equal(new Dictionary<string, long> { [File] = 4 });
    }

    [DatabaseFact]
    public async Task A_bad_name_is_refused()
    {
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());

        var response = await client.PostAsync($"/api/agent-logs/{Laptop()}/passwd?offset=0", Body("x\n"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task A_supervisor_cannot_write_an_agents_log()
    {
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var response = await client.PostAsync($"/api/agent-logs/{Laptop()}/{File}?offset=0", Body("x\n"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
