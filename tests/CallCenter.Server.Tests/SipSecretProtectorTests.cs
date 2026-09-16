using CallCenter.Server.Features.Auth;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The SIP secrets in the <c>users</c> table are encrypted at rest (N-05), so a
/// database dump does not hand over the extensions' passwords.
/// </summary>
public class SipSecretProtectorTests
{
    private const string Key = "PJkQ0h2rNfLm8vXcT3wYbA5sE7gU1dR9oI4pZ6nK2jM=";

    [Fact]
    public void A_secret_round_trips()
    {
        var protector = Create(Key);

        var stored = protector.Protect("s3cret-ext-101");

        stored.Should().NotBeNull().And.NotBe("s3cret-ext-101", "the stored value must not be the password");
        protector.Unprotect(stored).Should().Be("s3cret-ext-101");
    }

    [Fact]
    public void The_same_secret_encrypts_differently_each_time()
    {
        // A fresh nonce per call, so two agents with the same SIP password do
        // not have the same value sitting in the table.
        var protector = Create(Key);

        protector.Protect("same").Should().NotBe(protector.Protect("same"));
    }

    [Fact]
    public void A_secret_written_with_another_key_reads_as_unset()
    {
        // Rotating the key must degrade to "phone not configured" at login, not
        // throw and lock every agent out.
        var stored = Create(Key).Protect("s3cret");

        Create("a-different-key-entirely").Unprotect(stored).Should().BeNull();
    }

    [Fact]
    public void A_tampered_value_reads_as_unset()
    {
        var protector = Create(Key);
        var stored = protector.Protect("s3cret")!;

        // AES-GCM authenticates the ciphertext, so a flipped character fails the
        // tag check rather than decrypting to something else.
        var tampered = stored[..^2] + (stored[^1] == 'A' ? "BB" : "AA");

        protector.Unprotect(tampered).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-in-the-v1-format")]
    public void Values_that_are_not_ciphertext_read_as_unset(string? value)
    {
        Create(Key).Unprotect(value).Should().BeNull();
    }

    [Fact]
    public void Null_stays_null_when_protecting()
    {
        // An agent whose extensions have not been filled in yet.
        Create(Key).Protect(null).Should().BeNull();
    }

    [Fact]
    public void A_missing_key_fails_loudly_at_startup()
    {
        var act = () => Create(null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Security:SipSecretKey*");
    }

    private static ISipSecretProtector Create(string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SipSecretProtector.KeyConfigurationPath] = key,
            })
            .Build();

        return new SipSecretProtector(configuration, NullLogger<SipSecretProtector>.Instance);
    }
}
