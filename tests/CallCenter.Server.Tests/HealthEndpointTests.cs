using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Boots the real API in-process and checks the liveness endpoint. This is the
/// smoke test that the host, configuration, Serilog and DI wiring all compose.
/// </summary>
/// <remarks>
/// It deliberately needs no PostgreSQL, so it runs in CI without a database
/// service. That means turning off <c>Database:MigrateOnStartup</c>: the host
/// otherwise connects and applies migrations before serving, which is right in
/// production and wrong here. <c>/health</c> is a liveness check and does not
/// touch the database either.
/// </remarks>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("Database:MigrateOnStartup", "false"));
    }

    [Fact]
    public async Task Health_returns_200_and_reports_healthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
