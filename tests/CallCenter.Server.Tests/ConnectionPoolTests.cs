using CallCenter.Server.Data;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The server's pool stays below PostgreSQL's 100 connections (N-03). See
/// <see cref="ConnectionPool"/> for the shortage this prevents.
/// </summary>
public class ConnectionPoolTests
{
    private const string Production =
        "Host=127.0.0.1;Port=5432;Database=callcenter;Username=callcenter;Password=secret";

    [Fact]
    public void The_production_connection_string_is_capped_at_fifty() =>
        new NpgsqlConnectionStringBuilder(ConnectionPool.WithMaxSize(Production)).MaxPoolSize.Should().Be(50);

    [Fact]
    public void The_cap_is_below_what_PostgreSQL_allows_by_default() =>
        // 100 connections, three kept for a superuser. The backup, psql and a
        // migration need room beside the server.
        ConnectionPool.DefaultMaxSize.Should().BeLessThan(97);

    [Fact]
    public void Configuration_can_set_another_cap() =>
        new NpgsqlConnectionStringBuilder(ConnectionPool.WithMaxSize(Production, 20)).MaxPoolSize.Should().Be(20);

    [Theory]
    [InlineData("Maximum Pool Size=80")]
    [InlineData("maximum pool size = 80")]
    public void A_cap_written_in_the_connection_string_wins(string setting) =>
        new NpgsqlConnectionStringBuilder(ConnectionPool.WithMaxSize($"{Production};{setting}"))
            .MaxPoolSize.Should().Be(80);

    [Fact]
    public void Nothing_else_in_the_connection_string_changes()
    {
        var capped = new NpgsqlConnectionStringBuilder(ConnectionPool.WithMaxSize(Production));

        capped.Host.Should().Be("127.0.0.1");
        capped.Database.Should().Be("callcenter");
        capped.Username.Should().Be("callcenter");
        capped.Password.Should().Be("secret");
    }
}
