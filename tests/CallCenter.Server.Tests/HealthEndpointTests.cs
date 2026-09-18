using System.Net;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Boots the real API in-process and checks the liveness endpoint. This is the
/// smoke test that the host, configuration, Serilog, authentication and DI
/// wiring all compose.
/// </summary>
/// <remarks>
/// It deliberately needs no PostgreSQL, so it runs in CI without a database
/// service — see <see cref="CallCenterApiFactory"/> for what that costs.
/// <c>/health</c> is a liveness check and does not touch the database either.
/// </remarks>
[Collection(ApiCollection.Name)]
public class HealthEndpointTests(CallCenterApiFactory factory)
{
    [Fact]
    public async Task Health_returns_200_and_reports_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
