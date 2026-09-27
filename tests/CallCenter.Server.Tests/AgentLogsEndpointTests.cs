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
    public async Task A_supervisor_reads_what_an_agent_sent_and_an_agent_cannot()
    {
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var laptop = Laptop();

        await agent.PostAsync(
            $"/api/agent-logs/{laptop}/{File}?offset=0",
            Body("2026-09-27 09:02:00.000 +03:00 [ERR] Broken\n2026-09-27 09:03:00.000 +03:00 [INF] Fine\n"));

        var laptops = await supervisor.GetFromJsonAsync<List<AgentLogLaptopDto>>("/api/agent-logs");
        laptops!.Single(l => l.Laptop == laptop).Days.Single().Errors.Should().Be(1);

        var page = await supervisor.GetFromJsonAsync<AgentLogPageDto>(
            $"/api/agent-logs/{laptop}/{File}?levels={AgentLogLevels.Errors}");
        page!.Entries.Should().ContainSingle().Which.Text.Should().Be("Broken");

        (await agent.GetAsync("/api/agent-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync($"/api/agent-logs/{laptop}/{File}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supervisor.GetAsync($"/api/agent-logs/{laptop}/agent-20200101.log")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task A_supervisor_cannot_write_an_agents_log()
    {
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var response = await client.PostAsync($"/api/agent-logs/{Laptop()}/{File}?offset=0", Body("x\n"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
